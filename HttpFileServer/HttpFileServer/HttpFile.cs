using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace HttpFileServer
{
    public static class HttpFile
    {
        /// <summary>
        /// Reads the .http file and returns an object representing it.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static ConfigurationItem Read(string path)
        {
            var ser = new XmlSerializer(typeof(ConfigurationItem));
            try
            {
                if (File.Exists(path))
                    using (var reader = XmlReader.Create(path))
                        return (ConfigurationItem)ser.Deserialize(reader);
            }
            catch (FileNotFoundException)
            {
            }

            return null;
        }

        public abstract class ItemAction
        {
            [XmlAttribute("key")]
            public string Key { get; set; }
        }

        public class ItemToRemove : ItemAction
        {
        }

        [XmlRoot("configuration", IsNullable = false)]
        public class ConfigurationItem
        {
            [XmlElement("onStart")]
            public OnStartItem OnStart { get; set; }

            [XmlElement("handlers")]
            public HandlersList Handlers { get; set; }

            [XmlAttribute("port")]
            public string Port { get; set; }

            [XmlAttribute("host")]
            public string Host { get; set; }

            [XmlAttribute("httpRoot")]
            public string HttpRoot { get; set; }

            [XmlAttribute("serializeResponses")]
            public bool SerializeResponses { get; set; }

            [XmlAttribute("acceptHostPattern")]
            public string AcceptHostPattern { get; set; }
        }

        public class OnStartItem
        {
            [XmlAttribute("openInBrowser")]
            public string OpenInBrowser { get; set; }

            public override string ToString()
            {
                return string.Format(@"<onStart>");
            }
        }

        public class HandlersList
        {
            [XmlElement("add", typeof(HandlerItemToAdd))]
            [XmlElement("remove", typeof(ItemToRemove))]
            public List<ItemAction> Items { get; set; }

            public override string ToString()
            {
                return string.Format(@"<handlers>");
            }
        }

        public class HandlerItemToAdd : ItemAction
        {
            [XmlAttribute("type")]
            public string Type { get; set; }
        }
    }
}