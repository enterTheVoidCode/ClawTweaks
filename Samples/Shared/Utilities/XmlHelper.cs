using NLog;
using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Shared.Utilities
{
    public static class XmlHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static string ToXMLString<T>(T obj, bool compact = false)
        {
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = !compact,
                NewLineHandling = compact ? NewLineHandling.None : NewLineHandling.Replace,
                OmitXmlDeclaration = compact
            };

            using (var stringWriter = new StringWriter())
            using (var xmlWriter = XmlWriter.Create(stringWriter, settings))
            {
                var serializer = new XmlSerializer(typeof(T));
                serializer.Serialize(xmlWriter, obj);
                return stringWriter.ToString();
            }
        }

        public static T FromXMLString<T>(string xmlString)
        {
            var serializer = new XmlSerializer(typeof(T));
            var reader = new StringReader(xmlString);
            try
            {
                var obj = (T)serializer.Deserialize(reader);
                reader.Dispose();
                return obj;
            }
            catch (Exception e)
            {
                Logger.Error($"Exception {e} while deserializing \"{xmlString}\" into {typeof(T).Name}");
                return default;
            }
        }

        public static bool ToXMLFile<T>(T obj, string path)
        {
            var directoryName = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
            {
                Logger.Info($"Path {directoryName} not exist, trying to create it.");
                try
                {
                    Directory.CreateDirectory(directoryName);
                    Logger.Info($"New folder {directoryName} created.");
                }
                catch (Exception e)
                {
                    Logger.Error($"Got exception {e} while trying to create folder {directoryName}");
                }
            }

            var serializer = new XmlSerializer(typeof(T));
            using (var writer = new StreamWriter(path))
            {
                serializer.Serialize(writer, obj);
            }
            
            return true;
        }

        public static T FromXMLFile<T>(string path)
        {
            if (!File.Exists(path))
            {
                Logger.Warn($"{typeof(T).Name} not found at that {path}");
                return default;
            }

            var serializer = new XmlSerializer(typeof(T));
            var reader = new StreamReader(path);
            var obj = (T)serializer.Deserialize(reader);
            reader.Close();
            reader.Dispose();
            Logger.Info($"Loaded {typeof(T).Name} from {path}.");

            return obj;
        }
    }
}
