using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CloudPrint.Client.Model
{
    [DataContract]
    public class CloudPrinters
    {
        [DataMember]
        public bool success { get; set; }

        [DataMember]
        public List<CloudPrinter> printers { get; set; }
    }
}
