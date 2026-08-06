using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace VirtualLab.Infrastructure.Reporting
{
    public sealed class JsonReportWriter
    {
        private static readonly JsonSerializerSettings Settings =
            new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include,
                TypeNameHandling = TypeNameHandling.None
            };

        public string Write(ExperimentReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            return JsonConvert.SerializeObject(report, Settings);
        }
    }
}
