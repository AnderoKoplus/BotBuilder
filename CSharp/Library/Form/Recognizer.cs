﻿using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Bot.Builder.Form.Advanced
{
    /// <summary>
    /// Simple value table with explicitly added values.
    /// </summary>
    public class EnumeratedRecognizer : IRecognizer
    {
        public delegate string DescriptionDelegate(object value);
        public delegate IEnumerable<string> TermsDelegate(object value);

        public EnumeratedRecognizer(IFieldDescription description, Template helpFormat, IEnumerable<string> noPreference = null, IEnumerable<string> currentChoice = null)
        {
            _description = description.Description();
            _terms = description.Terms();
            _values = description.Values();
            _valueDescriptions = description.ValueDescriptions();
            _descriptionDelegate = (value) => description.ValueDescription(value);
            _termsDelegate = (value) => description.Terms(value);
            _helpFormat = helpFormat;
            _noPreference = noPreference;
            _currentChoice = currentChoice == null ? null : currentChoice.First();
            BuildPerValueMatcher(description.AllowNumbers(), noPreference, currentChoice);
        }

        public EnumeratedRecognizer(string description,
            IEnumerable<object> terms,
            IEnumerable<object> values,
            DescriptionDelegate descriptionDelegate,
            TermsDelegate termsDelegate,
            bool allowNumbers,
            PromptBase helpFormat,
            IEnumerable<string> noPreference = null,
            IEnumerable<string> currentChoice = null)
        {
            _values = values;
            _descriptionDelegate = descriptionDelegate;
            _termsDelegate = termsDelegate;
            _valueDescriptions = (from value in values select _descriptionDelegate(value)).ToArray();
            _helpFormat = helpFormat;
            BuildPerValueMatcher(allowNumbers, noPreference, currentChoice);
        }

        public IEnumerable<object> Values()
        {
            return _values;
        }

        public IEnumerable<string> ValueDescriptions()
        {
            return _valueDescriptions;
        }

        public string ValueDescription(object value)
        {
            return _descriptionDelegate(value);
        }

        public IEnumerable<string> ValidInputs(object value)
        {
            return _termsDelegate(value);
        }

        public string Help(object defaultValue)
        {
            var values = _valueDescriptions;
            if (defaultValue != null && _currentChoice != null)
            {
                values = values.Union(new string[] { _currentChoice });
            }
            if (_noPreference != null)
            {
                values = values.Union(new string[] { _noPreference.First() });
            }
            return string.Format(_helpFormat.Template(), 1, _max,
                Language.BuildList(values, _helpFormat.Separator, _helpFormat.LastSeparator));
        }

        public IEnumerable<TermMatch> Matches(string input, object defaultValue)
        {
            foreach (var expression in _expressions)
            {
                double longest = expression.Longest.Length;
                foreach (Match match in expression.Expression.Matches(input))
                {
                    var group1 = match.Groups[1];
                    var group2 = match.Groups[2];
                    if (group1.Success)
                    {
                        var confidence = System.Math.Min(group1.Length / longest, 1.0);
                        if (expression.Value is Special)
                        {
                            var special = (Special)expression.Value;
                            if (special == Special.CurrentChoice && (_noPreference != null || defaultValue != null))
                            {
                                yield return new TermMatch(group1.Index, group1.Length, confidence, defaultValue);
                            }
                            else if (special == Special.NoPreference && defaultValue != null)
                            {
                                yield return new TermMatch(group1.Index, group1.Length, confidence, null);
                            }
                        }
                        else
                        {
                            yield return new TermMatch(group1.Index, group1.Length, confidence, expression.Value);
                        }
                    }
                    else if (group2.Success)
                    {
                        yield return new TermMatch(group2.Index, group2.Length, 1.0, expression.Value);
                    }
                }
            }
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendFormat("EnumeratedRecognizer({0}", _description);
            builder.Append(" [");
            foreach (var description in _valueDescriptions)
            {
                builder.Append(" ");
                builder.Append(description);
            }
            builder.Append("])");
            return builder.ToString();
        }

        private enum Special { CurrentChoice, NoPreference };

        // Word character, any word character, any digit, any positive group over word characters
        private const string WORD = @"(\w|\\w|\\d|(\[(?>(\w|-)+|\[(?<number>)|\](?<-number>))*(?(number)(?!))\]))";
        private static Regex _wordStart = new Regex(string.Format(@"^{0}|\(", WORD), RegexOptions.Compiled);
        private static Regex _wordEnd = new Regex(string.Format(@"({0}|\))(\?|\*|\+|\{{\d+\}}|\{{,\d+\}}|\{{\d+,\d+\}})?$", WORD), RegexOptions.Compiled);

        private void BuildPerValueMatcher(bool allowNumbers, IEnumerable<string> noPreference, IEnumerable<string> currentChoice)
        {
            if (currentChoice != null)
            {
                // 0 is reserved for current default if any
                AddExpression(0, Special.CurrentChoice, currentChoice, allowNumbers);
            }
            var n = 1;
            foreach (var value in _values)
            {
                n = AddExpression(n, value, _termsDelegate(value), allowNumbers);
            }
            if (noPreference != null)
            {
                // Add recognizer for no preference
                n = AddExpression(n, Special.NoPreference, noPreference, allowNumbers);
            }
            if (_terms != null && _terms.Count() > 0)
            {
                // Add field terms to help disambiguate
                AddExpression(n, SpecialValues.Field, _terms, false);
            }
            _max = n - 1;
        }

        private int AddExpression(int n, object value, IEnumerable<string> terms, bool allowNumbers)
        {
            var orderedTerms = (from term in terms orderby term.Length descending select term).ToArray();
            var word = new StringBuilder();
            var nonWord = new StringBuilder();
            var first = true;
            var firstNonWord = true;
            foreach (var term in orderedTerms)
            {
                var nterm = term.Trim().Replace(" ", @"\s+");
                if (nterm == "") nterm = "qqqq";
                if (_wordStart.Match(nterm).Success && _wordEnd.Match(nterm).Success)
                {
                    if (first)
                    {
                        first = false;
                        word.Append(@"(\b(?:");
                    }
                    else
                    {
                        word.Append('|');
                    }
                    word.Append(@"(?:");
                    word.Append(nterm);
                    word.Append(')');
                }
                else
                {
                    if (firstNonWord)
                    {
                        firstNonWord = false;
                        nonWord.Append('(');
                    }
                    else
                    {
                        nonWord.Append('|');
                    }
                    nonWord.Append(@"(?:");
                    nonWord.Append(nterm);
                    nonWord.Append(')');
                }
            }
            if (first)
            {
                word.Append("(qqqq)");
            }
            else
            {
                if (allowNumbers)
                {
                    if (n == 0)
                    {
                        word.Append("|c");
                    }
                    else
                    {
                        word.AppendFormat(@"|{0}", n);
                    }
                }
                word.Append(@")\b)");
            }
            if (firstNonWord)
            {
                nonWord.Append("(qqqq)");
            }
            else
            {
                nonWord.Append(')');
            }
            ++n;
            var expr = string.Format("{0}|{1}",
                word.ToString(),
                nonWord.ToString());
            _expressions.Add(new ValueAndExpression(value, new Regex(expr, RegexOptions.IgnoreCase), orderedTerms.First()));
            return n;
        }

        private class ValueAndExpression
        {
            public ValueAndExpression(object value, Regex expression, string longest)
            {
                Value = value;
                Expression = expression;
                Longest = longest;
            }

            public readonly object Value;
            public readonly Regex Expression;
            public readonly string Longest;
        }

        private string _description;
        private IEnumerable<string> _noPreference;
        private string _currentChoice;
        private IEnumerable<string> _terms;
        private IEnumerable<object> _values;
        private IEnumerable<string> _valueDescriptions;
        private DescriptionDelegate _descriptionDelegate;
        private TermsDelegate _termsDelegate;
        private PromptBase _helpFormat;
        private int _max;
        private List<ValueAndExpression> _expressions = new List<ValueAndExpression>();
    }

    public class StringRecognizer : IRecognizer
    {
        public StringRecognizer(bool allowNull, IEnumerable<string> currentChoice)
        {
            _allowNull = allowNull;
            _currentChoices = new HashSet<string>(from choice in currentChoice select choice.Trim().ToLower());
        }

        public virtual IEnumerable<TermMatch> Matches(string input, object defaultValue = null)
        {
            var value = input.Trim();
            var matchValue = value.ToLower();
            if (defaultValue != null && (value == "" || matchValue == "c" || _currentChoices.Contains(matchValue)))
            {
                value = defaultValue as string;
            }
            if (value != null)
            {
                yield return new TermMatch(0, input.Length, 0.0, value);
            }
            else if (_allowNull == true)
            {
                yield return new TermMatch(0, 0, 0.0, null);
            }
        }

        public virtual IEnumerable<string> ValidInputs(object value)
        {
            yield return value as string;
        }

        public virtual string ValueDescription(object value)
        {
            return value as string;
        }

        public virtual IEnumerable<string> ValueDescriptions()
        {
            return new string[0];
        }

        public virtual IEnumerable<object> Values()
        {
            return null;
        }

        public virtual string Help(object defaultValue)
        {
            return "TODO";
        }

        protected bool _allowNull;
        protected HashSet<string> _currentChoices;
    }

    public delegate string TypeValue(object value, CultureInfo culture);
    public delegate IEnumerable<TermMatch> Matcher(string input);

    /// <summary>
    /// Regular expression recognizer.  For example if you had a DateTime field you would 
    /// have this format the date for the culture and use regexs to recognize date/times.
    /// </summary>
    public abstract class RegexRecognizer : IRecognizer
    {
        public RegexRecognizer(IFieldDescription fieldDescription)
        {
            _fieldDescription = fieldDescription;
        }

        public abstract IEnumerable<string> ValueDescriptions();

        public abstract string ValueDescription(object value);

        public IEnumerable<object> Values()
        {
            return null;
        }

        public abstract IEnumerable<string> ValidInputs(object value);

        public abstract string Help(object defaultValue);

        public abstract IEnumerable<TermMatch> Matches(string input, object defaultValue);

        protected IFieldDescription _fieldDescription;
    }

    /*
    public class LongRecognizer : RegexRecognizer
    {
        private static Regex _regex = new Regex(@"(?:^|\s+)(\d+)(?:\s+|$)", RegexOptions.Compiled);
        public LongRecognizer(IFieldDescription fieldDescription, string inputDescription, long min = long.MinValue, long max = long.MaxValue, string minMaxDescription = null)
            : base(fieldDescription)
        {
            _min = min;
            _max = max;
            _inputDescription = inputDescription;
            _minMaxDescription = minMaxDescription;
        }

        public override IEnumerable<string> ValueDescriptions()
        {
            if (_min != long.MinValue || _max != long.MaxValue)
            {
                yield return string.Format(_minMaxDescription, _min, _max);
            }
            else
            {
                yield return _inputDescription;
            }
        }

        public override string ValueDescription(object value)
        {
            return ((long)value).ToString(_fieldDescription.Culture().NumberFormat);
        }

        public override IEnumerable<string> ValidInputs(object value)
        {
            yield return ((long)value).ToString(_fieldDescription.Culture().NumberFormat);
        }

        public override IEnumerable<TermMatch> Matches(string input, object defaultValue, bool allowNull)
        {
            foreach (Match match in _regex.Matches(input))
            {
                if (match.Success)
                {
                    var group = match.Groups[1];
                    if (group.Success)
                    {
                        yield return new TermMatch(group.Index, group.Length, 1.0, group.Value);
                    }
                }
            }
        }

        string _inputDescription;
        private long _min;
        private long _max;
        private string _minMaxDescription;
    }

    public class DateRecognizer : RegexRecognizer
    {
        private static Regex _regex = new Regex(@"(?:^|\s)(?<Month>\d{1,2})/(?<Day>\d{1,2})/(?<Year>(?:\d{4}|\d{2}))(?:\s|$)", RegexOptions.Compiled);

        public DateRecognizer(IFieldDescription fieldDescription, string valueDescription)
            : base(fieldDescription)
        {
            _valueDescription = valueDescription;
        }

        public override IEnumerable<string> ValidInputs(object value)
        {
            yield return ((DateTime)value).ToString(_fieldDescription.Culture().DateTimeFormat);
        }

        public override IEnumerable<string> ValueDescriptions()
        {
            yield return _valueDescription;
        }

        public override string ValueDescription(object value)
        {
            return ((DateTime)value).ToString(_fieldDescription.Culture().DateTimeFormat);
        }

        public override IEnumerable<TermMatch> Matches(string input, object defaultValue, bool allowNull)
        {
            foreach (Match match in _regex.Matches(input))
            {
                if (match.Success)
                {
                    var group = match.Groups[0];
                    var month = int.Parse(match.Groups["Month"].Value);
                    var day = int.Parse(match.Groups["Day"].Value);
                    var year = int.Parse(match.Groups["Year"].Value);
                    if (year < 100) year += 2000;
                    var date = new DateTime();
                    bool ok = false;
                    try
                    {
                        date = new DateTime(year, month, day);
                        ok = true;
                    }
                    catch (Exception)
                    { }
                    if (ok)
                    {
                        yield return new TermMatch(group.Index, group.Length, 1.0, date);
                    }
                }
            }
        }

        private string _valueDescription;
    }
    */
}