using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using sema2012m;

namespace SimpleRDF
{
    class Query
    {
        public QueryTriplet[] triplets;
        public List<string> SelectParameters;
       // public TValue[] Parameters;
        public string[] ParametersNames;
        public List<string> FiterList;
        public QueryTriplet[] Optionals;
        public static Regex QuerySelectReg = new Regex(@"select\s+(?<selectGroups>((\?\w+\s+)+|\*))", RegexOptions.Compiled);
        public static Regex QueryWhereReg = new Regex(@"where\s+\{(?<whereGroups>([^{}]*\{[^{}]*\}[^{}]*)*|[^{}]*)\}", RegexOptions.Compiled);
        public static Regex TripletsReg = new Regex(
            @"((?<s>[^\s]+|'.*')\s+(?<p>[^\s]+|'.*')\s+(?<o>[^\s]+|'.*')\.(\s|$))|optional\s+{\s*(?<os>[^\s]+|'.*')\s+(?<op>[^\s]+|'.*')\s+(?<oo>[^\s]+|'.*')\s*}(\s|$)"
            , RegexOptions.Compiled);

        public TValue[] ParametesWithMultiValues;
        private List<string[]> parametrsValuesList = new List<string[]>();
       
        public Query(string filePath, Graph graph)
        {
            SelectParameters = new List<string>();
            var parameterTests = new Dictionary<TValue, List<QueryTriplet>>();
            var parametesWithMultiValues = new HashSet<TValue>();
            var tripletsList = new List<QueryTriplet>();
            var paramByName = new Dictionary<string, TValue>();
            var optionals=new List<QueryTriplet>();
            using (var f = new StreamReader(filePath))
            {
                var qs = f.ReadToEnd();
                var selectMatch = QuerySelectReg.Match(qs);
                if (selectMatch.Success)
                {
                    string parameters = selectMatch.Groups["selectGroups"].Value.Trim();
                    if (parameters != "*")
                        SelectParameters = parameters.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
                var whereMatch = QueryWhereReg.Match(qs);
                if (whereMatch.Success)
                {
                    string tripletsGroup = whereMatch.Groups["whereGroups"].Value;
                    foreach (Match tripletMatch in TripletsReg.Matches(tripletsGroup))
                    {
                        var s = tripletMatch.Groups["s"];
                        string p, o;
                        var ptriplet = new QueryTriplet();
                        bool isOptional=false, isData=false;
                        if (s.Success)
                        {
                            p = tripletMatch.Groups["p"].Value;
                            o = tripletMatch.Groups["o"].Value;
                        }
                        else if ((s = tripletMatch.Groups["os"]).Success)
                        {
                            p = tripletMatch.Groups["op"].Value;
                            o = tripletMatch.Groups["oo"].Value;
                            isOptional = true;
                        }
                        else throw new Exception("strange query triplet: " + tripletMatch.Value);
                        isData = o.StartsWith("'");
                        string sParamName = s.Value.Trim('\'', '>', '<');
                            ptriplet.S = TestParameter(sParamName, paramByName);
                            ptriplet.P = TestParameter(p=p.Trim('\'', '>', '<'), paramByName);
                            ptriplet.O = TestParameter(o=o.Trim('\'', '>', '<'), paramByName);
                        if (ptriplet.S.IsNewParametr) ptriplet.S.IsObject = true;
                        if(isOptional)
                            optionals.Add(ptriplet);
                        else if (ptriplet.S.IsNewParametr && ptriplet.O.IsNewParametr) // both params
                            tripletsList.Add(ptriplet);
                        else //!ptriplet.P.IsNewParametr
                            if (p == ONames.p_name) //if (ptriplet.S.IsNewParametr) meaning true
                                parametesWithMultiValues.Add(
                                    ptriplet.S.Interselect(graph.GetEntriesByName(o).Select(TValue.ItemCtor)));
                            else if (isData
                                     || ((p == ONames.rdftypestring || p == "a") && !ptriplet.O.IsNewParametr))
                            {
                                List<QueryTriplet> list;
                                if (!parameterTests.TryGetValue(ptriplet.S, out list))
                                    parameterTests.Add(ptriplet.S, list = new List<QueryTriplet>());
                                list.Add(ptriplet);
                            } /*object prop */
                            else if (ptriplet.S.IsNewParametr)
                            {
                                if (ptriplet.O.Item == null || !ptriplet.O.Item.ContainsKey(p))
                                    throw new NotImplementedException();//TODO
                                parametesWithMultiValues.Add(
                                    ptriplet.S.Interselect(
                                        ((HashSet<string>) ptriplet.O.Item[p]).Select(TValue.ItemCtor)));
                            }
                            else //if (ptriplet.O.IsNewParametr)
                            {
                                if (ptriplet.S.Item == null || !ptriplet.S.Item.ContainsKey(p))
                                    throw new NotImplementedException();//TODO
                                
                                parametesWithMultiValues.Add(
                                    ptriplet.O.Interselect((IEnumerable<string>) ptriplet.S.Item[p]));
                            }
                    }

                }  
            }
                        foreach (var entry in parameterTests)
                            if (entry.Key.Items != null)
                                foreach (var queryTriplet in entry.Value)
                                {
                                    QueryTriplet triplet = queryTriplet;
                                    entry.Key.Items = new HashSet<Item>(entry.Key.Items
                                        .Where(item => item.ContainsKey(triplet.P.Value)
                                                       &&
                                                       ((HashSet<string>)item[triplet.P.Value]).Contains(triplet.O.Value)));
                                }
                            else
                                tripletsList.AddRange(entry.Value);
            triplets = tripletsList.OrderByDescending(t => 
                parametesWithMultiValues.Contains(t.S)
                ? (parametesWithMultiValues.Contains(t.O) ? 2 : 1)
                : (parametesWithMultiValues.Contains(t.O) ? 1 : 0)).ToArray();
            Parameters = paramByName.Values.ToArray();
            ParametersNames = paramByName.Keys.ToArray();
            ParametesWithMultiValues = parametesWithMultiValues.ToArray(); //OrderBy(p => p.items.Count);
            Optionals = optionals.ToArray();
        }

        public void Run()
        {
          SetValueKnownParameters(0);
        }

        private void SetValueKnownParameters(int i)
        {
            Action forEachValue = ParametesWithMultiValues.Length == i+1
                ? (Action)(() => ObserveQuery(0)) 
                : (() => SetValueKnownParameters(i+1));
            var current = ParametesWithMultiValues[i];
            if(current.IsObject)
            foreach (var item in current.Items)
                {
                    current.SetValue(item);
                    forEachValue();
                }
            else
            {
                foreach (var item in current.DataVaues)
                {
                    current.SetValue(item);
                    forEachValue();
                }    
            }
        }

        public TValue[] Parameters { get; set; }


      private static TValue TestParameter(string spo, Dictionary<string,TValue> paramByName)
        {
            TValue value;
            if (!spo.StartsWith("?")) return new TValue {Value = spo};
            if (paramByName.TryGetValue(spo, out value)) return value;
            paramByName.Add(spo, value = new TValue {IsNewParametr = true, IsParametr = true});
            return value;
        }
        private void ObserveQuery(int currentQueryTripletItemIndex)
        {
            if (currentQueryTripletItemIndex == triplets.Length)
            {
                foreach (var parameter in Parameters.Where(p => !p.IsNewParametr))
                    parameter.IsClosed = true;
                ObserveOptional(0);
                foreach (var parameter in Parameters.Where(p => p.IsClosed))
                    parameter.IsClosed = false;
            }
            else
            {
                var cqt = triplets[currentQueryTripletItemIndex];
                TValue s = cqt.S,
                    p = cqt.P,
                    o = cqt.O;
                ObserveTriplet(currentQueryTripletItemIndex,
                    s, p, o,
                    !s.IsNewParametr,
                    !p.IsNewParametr,
                    !o.IsNewParametr);
            }
        }
        private void ObserveTriplet(int i, TValue s, TValue p, TValue o,
            bool hasValueS, bool hasValueP, bool hasValueO,
            Property predicate = null, bool isPredicateNull = true)
        {
            if (hasValueP)
            {
                if (hasValueS || hasValueO)
                {
                    if (isPredicateNull)
                    {
                        var item = (hasValueS ? s : o).Item;
                        if (!item.ContainsKey(p.Value)) return;
                        predicate = item[p.Value] as Property;
                    }
                    if (hasValueS && hasValueO)
                    {
                        if (predicate.Contains(o.Value))
                            ObserveQuery(i + 1);
                        return;
                    }
                    if (!predicate.IsObject && hasValueO) //Data predicate, O has value
                    {
                        foreach (var item in gr.GetSubjectsByProperty(p.Value, o.Value).Select(TValue.ItemCtor))
                            s.SetValue(item, true);
                        s.DropValue();
                        return;
                    }
                    var unknownValue = hasValueS ? o : s;
                    foreach (string values in predicate)
                    {
                        unknownValue.SetValue(values, hasValueO || predicate.IsObject);
                        ObserveQuery(i + 1);
                    }
                    unknownValue.DropValue();
                    return;
                }
                // s & o new params
                foreach (Item itm in gr.GetSubjectsByProperty(p.Value).Select(TValue.ItemCtor))
                {
                    if (!itm.ContainsKey(p.Value)) continue;
                    var pre = (Property)itm[p.Value];
                    s.SetValue(itm, true);
                    foreach (var v in  pre)
                    {
                        o.SetValue(v, pre.IsObject );
                        ObserveQuery(i + 1);
                    }
                }
                s.DropValue();
                o.DropValue();
                return;
            }
            throw new NotImplementedException();
            // p & (s or o) new params
            if (!hasValueS || !hasValueO)  //p  & (s xor o) new params
            {

                //if (!isNPs && !isNPo) 
                //    foreach (var property in
                //        (isNPo ? item.direct : item.inverse)
                //            .Where(property => property.Variants.Contains(p.V)))
                //    {
                //        p.SetValue(property.Predicate, isOpt);
                //        ObserveQuery(i + 1);
                //    }
                //else // p
                //{
                //    var properties = isNPs ? item.inverse : item.direct;
                //    foreach (var prp in properties)
                //        ObserveTriplet(i, s, p.SetValue(prp.Predicate, isOpt), o,
                //            isNPs, false, isNPo, isOpt, item, prp, false, false);
                //}
                //p.DropValue(false);
            }
            else // all params new
            {

            }
        }

        private void ObserveOptional(int i)
        {
            if(i==Optionals.Length)
                parametrsValuesList.Add(Parameters.Select(par => par.Value).ToArray());
            else
            {
                var current = Optionals[i];
                ObserveOptionalTriplet(current.S, current.P, current.O, i,
                    current.S.IsClosed || !current.S.IsParametr,
                    current.P.IsClosed || !current.P.IsParametr,
                    current.O.IsClosed || !current.O.IsParametr);
            }
        }

        public Graph gr;
        //   private static readonly Dictionary<Triplet<string>, bool> Cache = new Dictionary<Triplet<string>, bool>();
        public void ObserveOptionalTriplet(TValue s, TValue p, TValue o, int i,
            bool hasFixedValueS, bool hasFixedValueP, bool hasFixedValueO)
        {
            if (hasFixedValueP)
            {
                if (hasFixedValueS || hasFixedValueO)
                {
                    if (hasFixedValueS  && hasFixedValueO)
                        ObserveOptional(i + 1);
                    else
                    {
                        Property predicate;
                        Item known; TValue unknown;
                        if (hasFixedValueS){ known = s.Item;unknown = o;}
                        else{ known = o.Item;unknown = s;}
                        string oldValue = unknown.IsNewParametr ? null : unknown.Value;
                        bool oldIsObj = unknown.IsObject;
                        IEnumerable<string> newValues;
                        if (known != null && known.ContainsKey(p.Value)
                            && (newValues = predicate = (Property)known[p.Value]).Any())
                        {
                            if (oldValue != null)
                            {
                                ObserveOptional(i + 1);
                                newValues = newValues.Where(v => !ReferenceEquals(v, oldValue));
                            }
                            foreach (var oNewValue in newValues)
                            {
                                unknown.SetValue(oNewValue, hasFixedValueO || predicate.IsObject);
                                ObserveOptional(i + 1);
                            }
                            if (oldValue != null) unknown.SetValue(oldValue, oldIsObj);
                            else unknown.DropValue();
                        }
                        else
                        {
                            unknown.SetValue(string.Empty);
                            ObserveOptional(i + 1);
                            unknown.DropValue();
                        }
                    }
                }
                else
                {
                }
            }
            else return;
        }


        internal void OutputParamsAll(string outPath)
        {
            using (StreamWriter io = new StreamWriter(outPath, true))
                foreach (var parametrsValues in parametrsValuesList)
                {
                    for (int i = 0; i < parametrsValues.Length; i++)
                    {
                        io.WriteLine(String.Format("{0} {1}",
                            ParametersNames[i],
                           parametrsValues[i]));
                    }
                    io.WriteLine();
                }
        }

        internal void OutputParamsBySelect(string outPath)
        {
            var parametrsValuesIndexes = ParametersNames
                            .Select((e, i) => new { e, i });
            using (var io = new StreamWriter(outPath, true, Encoding.UTF8))
                foreach (var parametrsValues in parametrsValuesList)
                {
                    foreach (var i in SelectParameters
                        .Select(p => parametrsValuesIndexes.First(e => e.e == p)))
                    {
                        io.WriteLine(String.Format("{0}",
                            parametrsValues[i.i]));
                    }
                    io.WriteLine();
                }
        }
    }
}
