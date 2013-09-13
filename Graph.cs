using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using PolarDB;
using sema2012m;

namespace SimpleRDF
{
    public class Graph
    {
        private readonly string path;
        public Graph(string path)
        {
            if (path[path.Length - 1] != '\\' && path[path.Length - 1] == '/') path = path + "/";
            this.path = path;
            InitTypes();
            InitCells();
        }

        private void InitCells()
        {
            if (!File.Exists(path + "triplets.pac") 
                || !File.Exists(path + "graph_x.pxc")) return;
            triplets = new PaCell(tp_triplets, path + "triplets.pac");
            any_triplet = triplets.Root.Element(0);
            graph_x = new PxCell(tp_graph, path + "graph_x.pxc");
             n4_x = new PxCell(tp_graph, path + "n4.pxc");
        }

        // Для работы нужны первый и последний, остальные - для загрузки
        private PaCell triplets = null, quads = null, graph_a = null;
        private PxCell graph_x = null;
        private PaCell n4 = null; // это для таблицы имен
        private PxCell n4_x = null; // это для таблицы имен

        private PType tp_triplets, tp_quads, tp_graph, tp_n4;

        // Этот вход - служебный. Нужен для того, чтобы ему установить подходящий offset и получить через него доступ к триплету
        internal PaEntry any_triplet;

        public void Load(string[] rdf_files)
        {
            DateTime tt0 = DateTime.Now;
            // Закроем использование
            if (triplets != null) { triplets.Close(); triplets = null; }
            if (graph_x != null) { graph_x.Close(); graph_x = null; }
            // Создадим ячейки
            triplets = new PaCell(tp_triplets, path + "triplets.pac", false); 
            triplets.Clear();
            quads = new PaCell(tp_quads, path + "quads.pac", false);
            graph_a = new PaCell(tp_graph, path + "graph_a.pac", false);
            graph_x = new PxCell(tp_graph, path + "graph_x.pxc", false); graph_x.Clear();
            n4 = new PaCell(tp_n4, path + "n4.pac", false); n4.Clear();
            n4_x = new PxCell(tp_n4, path + "n4.pxc", false); n4.Clear();
            Console.WriteLine("cells initiated duration=" + (DateTime.Now - tt0).Ticks / 10000L); tt0 = DateTime.Now;

            TripletSerialInput(triplets, rdf_files);
            Console.WriteLine("After TripletSerialInput. duration=" + (DateTime.Now - tt0).Ticks / 10000L); tt0 = DateTime.Now;


            LoadQuadsAndSort();
            Console.WriteLine("After LoadQuadsAndSort(). duration=" + (DateTime.Now - tt0).Ticks / 10000L); tt0 = DateTime.Now;

            FormingSerialGraph(new SerialBuffer(graph_a, 3));
            Console.WriteLine("Forming serial graph ok. duration=" + (DateTime.Now - tt0).Ticks / 10000L); tt0 = DateTime.Now;

            // произвести объектное представление
            object g_value = graph_a.Root.Get().Value;
            graph_x.Fill2(g_value);
            Console.WriteLine("Forming fixed graph ok. duration=" + (DateTime.Now - tt0).Ticks / 10000L); tt0 = DateTime.Now;
            
            n4_x.Fill2(n4.Root.Get().Value);
            Console.WriteLine("Forming fixed n4 ok. duration=" + (DateTime.Now - tt0).Ticks / 10000L);

            // ========= Завершение загрузки =========
            // Закроем файлы и уничтожим ненужные
            triplets.Close();
            quads.Close(); File.Delete(path + "quads.pac");
            graph_a.Close(); File.Delete(path + "graph_a.pac");
            n4.Close(); File.Delete(path + "n4.pac");
            n4_x.Close();
            graph_x.Close();
            // Откроем для использования
            InitCells();
        }

        // =============== Методы доступа к данным ==================
        internal PxEntry GetEntryById(string id)
        {
            int e_hs = id.GetHashCode();
            return GetEntryById(e_hs);
        }

        private PxEntry GetEntryById(int e_hs)
        {
            PxEntry found = graph_x.Root.BinarySearchFirst(element =>
            {
                int v = (int) element.Field(0).Get().Value;
                return v < e_hs ? -1 : (v == e_hs ? 0 : 1);
            });
            return found;
        }

        public IEnumerable<PxEntry> GetEntriesByName(string name)
        {
            string n4=name.Length>4 ? name.Substring(0,4) : name;
            var foundPxN4 =
                n4_x.Root.BinarySearchFirst(element =>
                {
                    var s = element.Field(1).Get().Value as string;
                    return OnCompareN4(s, n4);
                });
            return ((int[])foundPxN4.Field(0).Get().Value)
                .Select(GetEntryById);
        }

        /// <summary>
        /// Класс, сохраняющий минимальную информацию о (найденной) сущности  
        /// </summary>
        public class EntityInfo
        {
            public string id;
            public PxEntry entry;
            public EntityInfo(string id, PxEntry entry) { this.id = id; this.entry = entry; }
            public string type = null; // RDF-тип
        }
        public XElement GetItemById(string id, XElement format)
        {
            EntityInfo ein = GetEntityInfoById(id);
            if (ein == null) return null;

            XElement result = new XElement("record", new XAttribute("id", id));
            XAttribute tatt = format.Attribute("type");
            string etype = ein.type;
            // Совмеcтимыми типами будут совпадающие или если tatt == null или etype == null. В случае несовместимости, возвращаем null
            if (tatt != null && etype != null && tatt.Value != etype) return null;
            // Обработаем поля, указанные в формате
            ProcessDirection(0, id, format, ein, result);
            ProcessDirection(1, id, format, ein, result);
            ProcessDirection(2, id, format, ein, result);
            return result;
        }
         private string[] directions = { "field", "direct", "inverse" };
         public static readonly int[] Fields = { 3, 1, 2 };
        private void ProcessDirection(int direction, string id, XElement format, EntityInfo ein, XElement result)
        {
            foreach (var f_el in format.Elements(directions[direction]))
            {
                string prop = f_el.Attribute("prop").Value;
                int hs = prop.GetHashCode();
                var dir_ent = ein.entry.Field(Fields[direction]);
                foreach (var p_rec in dir_ent.Elements())
                {
                    // Возьмем первый элемент и отфильтруем по несовпадению
                    int h = (int)p_rec.Field(0).Get().Value;
                    if (h != hs) continue;
                    // Теперь надо пройтись по списку и посмотреть реальные триплеты
                    foreach (var off_en in p_rec.Field(1).Elements())
                    {
                        long off = (long)off_en.Get().Value;
                        // Находим триплет
                        any_triplet.offset = off;
                        object[] tri_o = (object[])any_triplet.Get().Value;
                        int tag = (int)tri_o[0];
                        object[] rec = (object[])tri_o[1];
                        // Обрабатываем только "правильные"
                        if (direction == 0)
                        {
                            if (tag == 2 && (string)rec[0] == id && (string)rec[1] == prop)
                            {
                                result.Add(new XElement(directions[direction], new XAttribute("prop", prop),
                                    string.IsNullOrEmpty((string)rec[3]) ? null : new XAttribute(sema2012m.ONames.xmllang, rec[3]),
                                    rec[2]));
                            }
                        }
                        else if (direction == 1) // "direct"
                        {
                            if (tag == 1 && (string)rec[0] == id && (string)rec[1] == prop)
                            {
                                foreach (XElement fr in f_el.Elements("record"))
                                {
                                    var xr = GetItemById((string)rec[2], fr);
                                    if (xr != null)
                                    {
                                        result.Add(new XElement(directions[direction], new XAttribute("prop", prop),
                                            xr));
                                        break;
                                    }
                                }
                            }
                        }
                        else if (direction == 2) // "inverse"
                        {
                            if (tag == 1 && (string)rec[2] == id && (string)rec[1] == prop)
                            {
                                foreach (XElement fr in f_el.Elements("record"))
                                {
                                    var xr = GetItemById((string)rec[0], fr);
                                    if (xr != null)
                                    {
                                        result.Add(new XElement(directions[direction], new XAttribute("prop", prop),
                                            xr));
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Находит в графе определение сущности и извлекает из него минимальную информацию
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        internal EntityInfo GetEntityInfoById(string id)
        {
            PxEntry found = GetEntryById(id.GetHashCode());
            if (found.IsEmpty) return null;
            EntityInfo einfo = new EntityInfo(id, found);
            // Анализируем прямые объектные ссылки
            // Нас пока интересует только предикат типа
            string predicate_id = sema2012m.ONames.rdftypestring;
            int hs_type = predicate_id.GetHashCode();
            var direct_ent = found.Field(1);
            foreach (var p_rec in direct_ent.Elements())
            {
                // Возьмем первый элемент и отфильтруем по несовпадению
                int h = (int)p_rec.Field(0).Get().Value;
                if (h != hs_type) continue;
                // Теперь надо пройтись по списку и посмотреть реальные триплеты
                foreach (var off_en in p_rec.Field(1).Elements())
                {
                    long off = (long)off_en.Get().Value;
                    // Находим триплет
                    any_triplet.offset = off;
                    var tri_o = any_triplet.Get().Value;
                    Triplet tri = Triplet.Create(tri_o);
                    // Еще отбраковка
                    if (tri is OProp && tri.s == id && tri.p == predicate_id) { einfo.type = ((OProp)tri).o; break; }
                }
            }
            return einfo;
        }

        public IEnumerable<string> GetSubjectsByProperty(string property, string value=null)
        {
            yield break;
        }

        // ============ Технические методы ============
        private void FormingSerialGraph(ISerialFlow serial)
        {
            serial.StartSerialFlow();
            serial.S();

            int hs_e = Int32.MinValue;
            int vid = Int32.MinValue;
            int vidstate = 0;
            int hs_p = Int32.MinValue;

            bool firsttime = true;
            bool firstprop = true;
            foreach (FourFields record in from object[] el in quads.Root.Elements().Select(e => e.Value) 
                                          select new FourFields((int)el[0], (int)el[1], (int)el[2], (long)el[3]))
            {
                if (firsttime || record.e_hs != hs_e)
                { // Начало новой записи
                    firstprop = true;
                    if (!firsttime)
                    { // Закрыть предыдущую запись
                        serial.Se();
                        serial.Re();
                        serial.Se();
                        while (vid < 2 && vidstate <= 2)
                        {
                            serial.S();
                            serial.Se();
                            vidstate += 1;
                        }
                        serial.Re();
                    }
                    vidstate = 0;
                    hs_e = record.e_hs;
                    serial.R();
                    serial.V(record.e_hs);
                    vid = record.vid;
                    while (vidstate < vid)
                    {
                        serial.S();
                        serial.Se();
                        vidstate += 1;
                    }
                    vidstate += 1;
                    serial.S();
                }
                else if (record.vid != vid)
                {
                    serial.Se();
                    serial.Re();
                    firstprop = true;

                    serial.Se();
                    vid = record.vid;
                    while (vid != vidstate)
                    {
                        serial.S();
                        serial.Se();
                        vidstate += 1;
                    }
                    vidstate += 1;
                    serial.S();
                }

                if (firstprop || record.p_hs != hs_p)
                {
                    hs_p = record.p_hs;
                    if (!firstprop)
                    {
                        serial.Se();
                        serial.Re();
                    }
                    firstprop = false;
                    serial.R();
                    serial.V(record.p_hs);
                    serial.S();
                }
                serial.V(record.off);
                firsttime = false;
            }
            if (!firsttime)
            { // Закрыть последнюю запись
                serial.Se();
                serial.Re();
                serial.Se();
                while (vid < 2 && vidstate <= 2)
                {
                    serial.S();
                    serial.Se();
                    vidstate += 1;
                }
                serial.Re();
            }
            serial.Se();
            serial.EndSerialFlow();
        }
        private struct FourFields
        {
            public int e_hs, vid, p_hs;
            public long off;
            public FourFields(int a, int b, int c, long d)
            {
                this.e_hs = a; this.vid = b; this.p_hs = c; this.off = d;
            }
        }

        private void LoadQuadsAndSort()
        {
            n4.StartSerialFlow();
            n4.S();
            quads.StartSerialFlow();
            quads.S();
            foreach (var tri in triplets.Root.Elements())
            {
                object[] tri_uni = (object[])tri.Value;
                int tag = (int)tri_uni[0];
                object[] rec = (object[])tri_uni[1];
                int hs_s = ((string)rec[0]).GetHashCode();
                int hs_p = ((string)rec[1]).GetHashCode();
                if (tag == 1) // объектое свойство
                {
                    int hs_o = ((string)rec[2]).GetHashCode();
                    quads.V(new object[] { hs_s, 0, hs_p, tri.Offset });
                    quads.V(new object[] { hs_o, 1, hs_p, tri.Offset });
                }
                else // поле данных
                {
                    quads.V(new object[] { hs_s, 2, hs_p, tri.Offset });
                    if ((string) rec[1] != ONames.p_name) continue; 
                    // Поместим информацию в таблицу имен n4
                    string name = (string)rec[2];
                    string name4 = name.Length <= 4 ? name : name.Substring(0, 4);
                    n4.V(new object[] { hs_s, name4.ToLower() });
                }
            }
            quads.Se();
            quads.EndSerialFlow();
            n4.Se();
            n4.EndSerialFlow();

            // Сортировка квадриков
            quads.Root.Sort((o1, o2) =>
            {
                object[] v1 = (object[])o1;
                object[] v2 = (object[])o2;
                int e1 = (int)v1[0];
                int e2 = (int)v2[0];
                int q1 = (int)v1[1];
                int q2 = (int)v2[1];
                int p1 = (int)v1[2];
                int p2 = (int)v2[2];
                return e1 < e2 ? -3 : (e1 > e2 ? 3 :
                    (q1 < q2 ? -2 : (q1 > q2 ? 2 :
                    (p1 < p2 ? -1 : (p1 > p2 ? 1 : 0)))));
            });
            // Сортировка таблицы имен
            n4.Root.Sort(OnCompareN4);
        }

        private int OnCompareN4(object o1, object o2)
        {
            object[] v1 = (object[]) o1;
            object[] v2 = (object[]) o2;
            string s1 = (string) v1[1];
            string s2 = (string) v2[1];
            return String.Compare(s1, s2, StringComparison.Ordinal);
        }

        private static void TripletSerialInput(ISerialFlow sflow, IEnumerable<string> rdf_filenames)
        {
            sflow.StartSerialFlow();
            sflow.S();
            foreach (string db_falename in rdf_filenames)
                ReadXML2Quad(db_falename, (id, property, value, isObj, lang) =>
                    sflow.V(isObj
                        ? new object[] {1, new object[] {id, property, value}}
                        : new object[] {2, new object[] {id, property, value, lang ?? ""}}));
            sflow.Se();
            sflow.EndSerialFlow();
        }
        private delegate void QuadAction(string id, string property,
             string value, bool isObj = true, string lang = null);

        private static string langAttributeName = "xml:lang",
            rdfAbout = "rdf:about",
               rdfResource = "rdf:resource",
               NS = "http://fogid.net/o/";

        private static void ReadXML2Quad(string url, QuadAction quadAction)
        {
            string resource;
            bool isObj;
            string id = string.Empty;
            using (var xml = new XmlTextReader(url))
                while (xml.Read())
                    if (xml.IsStartElement())
                        if (xml.Depth == 1 && (id = xml[rdfAbout]) != null)
                            quadAction(id, ONames.rdftypestring, NS + xml.Name);
                        else if (xml.Depth == 2 && id != null)
                            quadAction(id, NS + xml.Name,
                                isObj: isObj = (resource = xml[rdfResource]) != null,
                                lang: isObj ? null : xml[langAttributeName],
                                value: isObj ? resource : xml.ReadString());
        }


        private void InitTypes()
        {
            this.tp_triplets =
                new PTypeSequence(
                    new PTypeUnion(
                        new NamedType("empty", new PType(PTypeEnumeration.none)), // не используется, нужен для выполнения правила атомарного варианта
                        new NamedType("op",
                            new PTypeRecord(
                                new NamedType("subject", new PType(PTypeEnumeration.sstring)),
                                new NamedType("predicate", new PType(PTypeEnumeration.sstring)),
                                new NamedType("obj", new PType(PTypeEnumeration.sstring)))),
                        new NamedType("dp",
                            new PTypeRecord(
                                new NamedType("subject", new PType(PTypeEnumeration.sstring)),
                                new NamedType("predicate", new PType(PTypeEnumeration.sstring)),
                                new NamedType("data", new PType(PTypeEnumeration.sstring)),
                                new NamedType("lang", new PType(PTypeEnumeration.sstring))))));
            this.tp_quads =
                new PTypeSequence(
                    new PTypeRecord(
                        new NamedType("hs_e", new PType(PTypeEnumeration.integer)),
                        new NamedType("vid", new PType(PTypeEnumeration.integer)),
                        new NamedType("hs_p", new PType(PTypeEnumeration.integer)),
                        new NamedType("off", new PType(PTypeEnumeration.longinteger))));
            this.tp_graph = new PTypeSequence(new PTypeRecord(
                new NamedType("hs_e", new PType(PTypeEnumeration.integer)),
                new NamedType("direct",
                    new PTypeSequence(
                        new PTypeRecord(
                            new NamedType("hs_p", new PType(PTypeEnumeration.integer)),
                            new NamedType("off", new PTypeSequence(new PType(PTypeEnumeration.longinteger)))))),
                new NamedType("inverse",
                    new PTypeSequence(
                        new PTypeRecord(
                            new NamedType("hs_p", new PType(PTypeEnumeration.integer)),
                            new NamedType("off", new PTypeSequence(new PType(PTypeEnumeration.longinteger)))))),
                new NamedType("data",
                    new PTypeSequence(
                        new PTypeRecord(
                            new NamedType("hs_p", new PType(PTypeEnumeration.integer)),
                            new NamedType("off", new PTypeSequence(new PType(PTypeEnumeration.longinteger))))))));
            tp_n4 = new PTypeSequence(new PTypeRecord(
                new NamedType("hs_e", new PType(PTypeEnumeration.integer)),
                new NamedType("s4", new PTypeFString(4))));
        }
    }
}
