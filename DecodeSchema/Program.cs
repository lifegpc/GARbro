using GameRes;
using GameRes.Compression;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DecodeSchema {
    internal class Program {
        static void Main(string[] args) {
            DecodeSchema decodeSchema = new DecodeSchema();
            decodeSchema.decode();
        }
    }
    internal class DecodeSchema {
        public void decode() {
            DeserializeGameData();
        }
        void DeserializeGameData() {
            string scheme_file = Path.Combine(FormatCatalog.Instance.DataDirectory, "Formats.dat");
            try {
                using (var file = File.OpenRead(scheme_file)) {
                    var version = FormatCatalog.Instance.GetSerializedSchemeVersion(file);
                    using (var zs = new ZLibStream(file, CompressionMode.Decompress, true)) {
                        var bin = new BinaryFormatter();
                        var db = (SchemeDataBase)bin.Deserialize(zs);
                        string json = JsonConvert.SerializeObject(db, Formatting.Indented,
                            new JsonSerializerSettings {
                                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                            });
                        File.WriteAllText("Formats.json", json, Encoding.UTF8);
                    }
                }
            } catch (Exception X) {
                Console.Error.WriteLine("Scheme deserialization failed: {0}", X.Message);
            }
        }
    }
}
