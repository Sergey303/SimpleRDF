using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SimpleRDF
    {
       public enum TypeVariants
            {
                String,Double, Bool, Time
            }
       class MultiType
        {
            //public string s;
            //public double d;
            //public bool b;
            //public TimeSpan date;
            public object Value;
          //  public bool isString, isDouble, isBool, isTimeSpan;
            
           public TypeVariants Type;
            //public MultiType(string s)
            //{
            //     double d;
            //     bool b;
            //     TimeSpan date;
            //   if(Double.TryParse(s,out d))
            //   {
            //       Type = TypeVariants.Double;
            //       value = d;
            //       return;
            //   }
            //   if(TimeSpan.TryParse(s, out date))
            //   {
            //       Type = TypeVariants.Time;
            //       value = date;
            //       return;
            //   }
            //   if (Boolean.TryParse(s, out b))
            //    {
            //        Type = TypeVariants.Bool;
            //        value = b;
            //        return;
            //    }
            //   value = s;
            //   Type = TypeVariants.String;
            //}
           public static MultiType operator+(MultiType left,MultiType right)
            {
                if (left.Type == TypeVariants.Double && right.Type == TypeVariants.Double)
                    return new MultiType { Type = TypeVariants.Double, Value = (double)left.Value + (double)right.Value };
                if (left.Type == TypeVariants.String && right.Type == TypeVariants.String)
                    return new MultiType { Type = TypeVariants.String, Value = (string)left.Value + (string)right.Value };
                if (left.Type == TypeVariants.Time && right.Type == TypeVariants.Time)
                    return new MultiType { Type = TypeVariants.Time, Value = (TimeSpan)left.Value + (TimeSpan)right.Value };
                throw new Exception("any type operator+");
            }
           
           ///Safe Mode
           //public bool AsBool()
           // { return isBool && (bool)value; }
        }
    class FilterItem 
    {
      
        /// <summary>
        /// FILTER regex(?title, "^SPARQL")
        /// FILTER (?v < 3)
        /// </summary>
        public string ExpressionString;
        /// <summary>
        /// Dynamicly created function.
        /// Run Filter on parametrs.
        /// </summary>
        public Func<Dictionary<string, string>, bool> Test;

        private static readonly Regex RegFilteRregex = new Regex("FILTER regex\\(\\?(?<paramter>\\w+), \"(?<regExpression>[^\"]*)\"(, \"(?<ps>[ismx]*)\")*\\)", RegexOptions.Compiled);
        private static readonly Regex regFILTER = new Regex("FILTER \\((?<regExpression>w+)\\)", RegexOptions.Compiled);
        private static Regex regParentes = new Regex("\\((?<inside>[^)^(])\\)", RegexOptions.Compiled);
        private static readonly Regex RegSum = new Regex("(?<left>[^+])\\+(?<right>[.])", RegexOptions.Compiled);
        private static readonly Regex RegDiff = new Regex("(?<left>[^-])\\-(?<right>[.])", RegexOptions.Compiled);
        private static readonly Regex RegMul = new Regex("(?<left>[^*])\\*(?<right>[.])", RegexOptions.Compiled);
        private static readonly Regex RegDiv = new Regex("(?<left>[^/])\\/(?<right>[.])", RegexOptions.Compiled);
        private static readonly Regex RegMoreThen = new Regex("(?<left>[^<=])<s*=(?<right>[.])", RegexOptions.Compiled);

        public HashSet<string> parametersNames;

        public bool IsFilter(string s)
        {
            return regFILTER.Match(s).Success;
        }
        public static Dictionary<string, Func<Dictionary<string, string>, bool>> FuncStore = new Dictionary<string, Func<Dictionary<string, string>, bool>>();
        public bool Filter(string s, Dictionary<string, string> parameters)
        {
            Func<Dictionary<string, string>, bool> func;// = F(s);
            if (!FuncStore.TryGetValue(s, out func))
                FuncStore.Add(s, func = Fdynamic2Fbool(F(s)).Cache());
            return func(parameters);
        }
        Func<Dictionary<string, string>, bool> Fdynamic2Fbool(Func<Dictionary<string, string>, dynamic> f)
        {
            return arg =>(bool)f(arg);
        }

        private Func<Dictionary<string, string>, dynamic> F(string s)
        {

            if (parametersNames.Contains(s))
                return ps=> F(ps[s])(ps);

            double d;
            bool b;
            TimeSpan date;
            //MultiType t = new MultiType();
            if (Double.TryParse(s, out d))
                return ps => d;//new MultiType { Type = TypeVariants.Double, value = d };
            
            if (TimeSpan.TryParse(s, out date))
                return ps => date;// new MultiType { Type = TypeVariants.Time, value = date };                
            
            if (Boolean.TryParse(s, out b))
                return ps => b;// new MultiType { Type = TypeVariants.Bool, value = b };                
            




            Match m;
            //if ((m = regParentes.Match(s)).Success)
            //  s.Replace("(" + m.Groups["inside"] + ")", "f1");




            Func<dynamic, dynamic, dynamic> binOperator = null;
           Func<Dictionary<string, string>, dynamic> left = null, right = null;
           if ((m = RegSum.Match(s)).Success)
               binOperator = (l, r) => l + r;//l.isDouble && r.isDouble date = l.date + r.date 
           else if ((m = RegDiff.Match(s)).Success)
                binOperator = (l, r) => l - r;
           else if ((m = RegMul.Match(s)).Success)
               binOperator = (l, r) => l / r;
           else if ((m = RegDiv.Match(s)).Success)
               binOperator = (l, r) => l * r;
           else if ((m = RegMoreThen.Match(s)).Success)
               binOperator = (l, r) => l < r; 
            left = F(m.Groups["left"].Value);

            right = F(m.Groups["right"].Value);

            return ps => binOperator(left(ps), right(ps));
            //if(Double.TryParse(s, out dres))
            //return ps => true;
         ///   return ps => s;
        }

        public void Convert()
        {
            var match = RegFilteRregex.Match(ExpressionString);
            if(match.Success)
            {
                var parameter = match.Groups["paramter"].Value;
                var regExpression = match.Groups["regExpression"].Value;
                var ismx = match.Groups["ps"] != null ? match.Groups["ps"].Value : "";
                Test = parameters => Regex(parameters[parameter], regExpression, ismx);
            }            
        }

        
        public static bool Regex(string text, string pattern, string flags)
        {
            Regex regularExpression;
            RegexOptions options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
            if (flags != null)
            {           
                if (flags.Contains("i"))                
                    options = options | RegexOptions.IgnoreCase;
                
                if (flags.Contains("s"))                
                    options = options | RegexOptions.Singleline;
                
                if (flags.Contains("m"))                
                    options = options | RegexOptions.Multiline;
                if (flags.Contains("x"))
                    options = options | RegexOptions.IgnorePatternWhitespace;      
            }
            regularExpression = new Regex(pattern, options);
            return regularExpression.Match(text).Success;
        }
        public static bool LangMatches(string languageTag, string languageRange)
        {
            if (languageRange == "*") return languageTag != string.Empty;
            return languageTag.ToLower().Contains(languageRange.ToLower());
        }

        public static bool SameTerm(string termLeft, string termRight)
        {            
            var leftSubStrings=termLeft.Split(new[]{"^^"}, StringSplitOptions.RemoveEmptyEntries);
            var rightSubStrings=termRight.Split(new[]{"^^"}, StringSplitOptions.RemoveEmptyEntries);
            if (leftSubStrings.Length == 2 && rightSubStrings.Length == 2)
            {      
                //different types
                if (rightSubStrings[1].ToLower() != leftSubStrings[1].ToLower()) return false;                
                ///TODO: different namespaces and same types
            }
                double leftDouble, rightDouble;
                DateTime leftDate, rightDate;
                return rightSubStrings[0]==leftSubStrings[0]
                       ||
                       (Double.TryParse(leftSubStrings[0].Replace("\"", ""), out leftDouble)
                                      &&
                        Double.TryParse(rightSubStrings[0].Replace("\"", ""), out rightDouble)
                                      &&
                        leftDouble == rightDouble)
                        ||
                        (DateTime.TryParse(leftSubStrings[0].Replace("\"", ""), out leftDate)
                                      &&
                        DateTime.TryParse(rightSubStrings[0].Replace("\"", ""), out rightDate)
                                      &&
                        leftDate == rightDate);                    
        }
        
        public static string Lang(string term)
        {
            var substrings = term.Split('@');
            if (substrings.Length == 2)
                return substrings[1];
            return string.Empty;
        }
       
    }
       public static class Extensions
       {
           public static Func<Tin, Tout> Cache<Tin,Tout>(this Func<Tin,Tout> f)
           {
               Dictionary<Tin, Tout> cache = new Dictionary<Tin, Tout>();
               return p =>
               {
                   Tout r;
                   if (!cache.TryGetValue(p, out r))
                       cache.Add(p, r = f(p));
                   return r;

               };
           }
       }
    }
