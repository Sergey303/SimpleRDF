using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PolarDB;

namespace SimpleRDF
{
    public abstract class Triplet
    {
        public string s, p;
        /// <summary>
        /// Порождает объект класса Triplet по объектному представлению триплета 
        /// </summary>
        /// <param name="valu">Объектное представление триплета</param>
        /// <returns></returns>
        public static Triplet Create(object valu)
        {
            object[] uni = (object[])valu;
            int tag = (int)uni[0];
            object[] rec = (object[])uni[1];
            if (tag == 1) return new OProp((string)rec[0], (string)rec[1], (string)rec[2]);
            else if (tag == 2) return new DProp((string)rec[0], (string)rec[1], (string)rec[2], (string)rec[3]);
            else throw new Exception("Can't create instance of Triplet class");
        }
    }
    public class OProp : Triplet
    {
        public string o;
        public OProp(string s, string p, string o) { this.s = s; this.p = p; this.o = o; }
    }
    public class DProp : Triplet
    {
        public string d; public string lang;
        public DProp(string s, string p, string d) { this.s = s; this.p = p; this.d = d; }
        public DProp(string s, string p, string d, string l) { this.s = s; this.p = p; this.d = d; this.lang = l; }
    }

    public class TValue
    {
        public static Func<string, Item> ItemCtor;
        private Item item;
        public bool IsParametr;
        public bool IsNewParametr;
        public bool IsObject;
        public string Value;
        internal HashSet<Item> Items;
        private HashSet<string> dataVaues;
        
        public Item Item
        {
            get { return item ?? (item=ItemCtor(Value)); }
        }

        public IEnumerable<object> Values
        {
            get
            {
                if (IsObject) return Items;
                return dataVaues;
            }
        }
        public TValue Interselect(IEnumerable<Item> value)
        {
            if (!IsObject)
            {
                IsObject = true;
                if (dataVaues != null)
                    Interselect(dataVaues.Select(ItemCtor));
            }
            if (Items == null)
                Items = new HashSet<Item>(value);
            else
                Items.IntersectWith(value);
            return this;
        }
        public TValue Interselect(IEnumerable<string> value)
        {
            if (IsObject)
                Interselect(value.Select(ItemCtor));
            else if (dataVaues == null)
                dataVaues = new HashSet<string>(value);
            else
                dataVaues.IntersectWith(value);
            return this;
        }


        public TValue SetValue(object value, bool isObject)
        {
            IsObject = isObject;
            return SetValue(value);
        }
        public TValue SetValue(object value)
        {
            //if (value == null) return;
            IsNewParametr = false;
            if (value is string)
            {
                if (ReferenceEquals(value, Value)) return this;
                Value = (string)value;
                if (IsObject)
                    item = null;
            }
            else if (value is Item)
            {
                IsObject = true;
                item = (Item)value;
                Value = item.Id;
            }
            return this;
        }
        public void DropValue()
        {
            IsNewParametr = true;
        }

        public bool IsClosed;
        
    }

    public class QueryTriplet
    {
        public TValue S, P ,O;
    }

    public class Item:Hashtable
    {
        public string Id;

        public Item(PxEntry entry, Graph gr): this(entry, gr, null)
        {
        }

        public Item(PxEntry entry, Graph gr, string id):base(CreateContainer(entry, gr, ref id))
        {
            Id = id;
        }
        public static Dictionary<object, Property> CreateContainer(PxEntry entry, Graph gr, ref string id)
        {
        var container = new Dictionary<object, Property>();
            for (int direction = 0; direction < 3; direction++)
                foreach (long off in entry.Field(Graph.Fields[direction]).Elements()
                    .SelectMany(pRec =>
                        pRec.Field(1).Elements()
                            .Select(offEn => (long)offEn.Get().Value)))
                {
                    // Находим триплет
                    gr.any_triplet.offset = off;
                    object[] tri_o = (object[]) gr.any_triplet.Get().Value;
                    int tag = (int)tri_o[0];
                    object[] rec = (object[])tri_o[1];
                    int hash;
                    // Обрабатываем только "правильные"&& (string)rec[0] == id
                    Property property;
                    if (direction == 0 && tag == 2)
                    {
                        id = (string) rec[0];
                        if (!container.TryGetValue(rec[1], out property))
                            container.Add(rec[1], property = new Property {IsObject = false});
                        property.Add((string) rec[2]);
                    }
                    else if (direction == 1 && tag == 1) // "direct"&& rec[0].GetHashCode() == id
                    {
                        id = (string)rec[0];
                        if (!container.TryGetValue(rec[1], out property))
                            container.Add(rec[1], property = new Property{IsObject = true});
                        property.Add((string) rec[2]);
                    }
                    else if (direction == 2 && tag == 1) // "inverse"(string)rec[2] != id
                    {
                        id = (string)rec[2];
                        if (!container.TryGetValue(rec[1], out property))
                            container.Add(rec[1], property = new Property { IsObject = true });
                        property.Add((string) rec[0]);
                    }
                }
            return container;
        }
    }

    public class Property: HashSet<string>
    {
        //  public bool Direction;
          public bool IsObject;
        //public string Name;
    }
}
