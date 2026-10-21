using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace OledGuard
{
    public static class I18n
    {
        private static readonly Dictionary<string, string> translations;
        public static string Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";
        static I18n()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("translations.json"))
                translations = (Dictionary<string, string>)new DataContractJsonSerializer(typeof(Dictionary<string, string>),
                    new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(stream);
        }
        public static string T(string text)
        {
            string exact;
            if (translations.TryGetValue(text, out exact)) return Language == "en" ? exact : text;
            var reverse = translations.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Key.Length).First().Key);
            if (reverse.TryGetValue(text, out exact)) return Language == "en" ? text : exact;
            var table = Language == "en" ? translations : reverse;
            string pattern = string.Join("|", table.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
            return Regex.Replace(text, pattern, m => table[m.Value]);
        }
    }
}
