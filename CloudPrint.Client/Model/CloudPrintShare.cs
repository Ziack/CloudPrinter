using System.Runtime.Serialization;

namespace CloudPrint.Client.Model
{
    [DataContract]
    public class CloudPrintShare
    {
        [DataMember]
        public bool success { get; set; }

        [DataMember]
        public string message { get; set; }

    }
}
