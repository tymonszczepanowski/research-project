
using Microsoft.VisualBasic;
using MongoDB.Driver;
using MongoDB.Bson;
using Models;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.IO;

namespace Generator
{
    class Generator {
        private static List<string> contactPoints = [];
        private static List<MongoClient> clients = [];
        private static List<IMongoCollection<Record>> collections = [];
        private static DateTime start_date;
        static void Main(string[] args)
        {

            start_date = DateTime.ParseExact("20230501T00:00:00Z", "yyyyMMddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
            ParseArgs(args);
            Connect();
            //ResetDB();
            //CreateTSCollection();
            //BulkLoad(1_000);
            //ReadTest(100);
            AggregationTest(1, false);
            // ReadTest(1_000_000,0);
            //watch.Stop();
            //Console.WriteLine(watch.ElapsedMilliseconds);
           
        }
        static void ParseArgs(string[] args){
            if(args.Contains("-urls")){
                int idx = Array.IndexOf(args, "-urls");
                int no_urls = -1;
                try{
                    no_urls = int.Parse(args[idx+1]);
                }
                catch(IndexOutOfRangeException e){
                    Console.WriteLine(e);
                    Console.WriteLine("Number of urls not specified");
                }
                try{
                    for (int i = 2; i < no_urls+2; i++){
                        contactPoints.Add(args[idx + i]);
                    }
                }
                catch(IndexOutOfRangeException e){
                    Console.WriteLine(e);
                    Console.Write("Number of urls given doesn't match number specified.");
                }
            }
        }
        static void Connect(){
            foreach (var conString in contactPoints){
                clients.Add(new MongoClient("mongodb://root:pass@localhost:27017/"));
            }
        }
        static void ResetDB(){
            var master = clients[0];
            master.DropDatabase("benchmark");
        }
        static void CreateTSCollection(){
            clients[0].GetDatabase("benchmark").CreateCollection("metrics", new CreateCollectionOptions { TimeSeriesOptions = new TimeSeriesOptions("timestamp", "data") });
        }
        static void BulkLoad(int n){
            var data = GenerateTestData(n, start_date);
            using (var session = clients[0].StartSession()){
                var db = clients[0].GetDatabase("benchmark");
                db.GetCollection<Record>("metrics").InsertMany(data);
            }
        }
        static List<Record> GenerateTestData(int count, DateTime start){
            var testData = new List<Record>();
            var random = new Random();
            Record r;
            for (var i = 0; i < count; i++){
              
                for (int bs_id = 0; bs_id < 3; bs_id++){
                        r = new Record { 
                        timestamp = start.AddSeconds(i).ToUniversalTime(), 
                        BSData = new BSData{
                                bs_id = bs_id
                            } 
                        };
                    testData.Add(r);
                    for (int ue_id = 0; ue_id < 5; ue_id++){
                            r = new Record { 
                            timestamp = start.AddSeconds(i).ToUniversalTime(), 
                            UEData = new UEData{
                                    ue_id = ue_id,
                                    pci = bs_id 
                                } 
                        };
                        testData.Add(r);  
                    }
                }
                
                
                
            }

            return testData;
        }

       static void ReadTest(int no_reads){
            var watch = new System.Diagnostics.Stopwatch();
            Console.WriteLine($"Starting read test with {no_reads} reads...");
            double time_sum = 0;
            for (int i = 0; i < no_reads; i++){
                var db = clients[0].GetDatabase("benchmark");
                var collection = db.GetCollection<Record>("metrics");
                var filter = Builders<Record>.Filter.Eq(x=>x.BSData.bs_id, new Random().Next(3));
                watch.Start();
                var doc = collection.Find<Record>(filter).First();
                watch.Stop();
                time_sum += watch.Elapsed.TotalSeconds;
            }
            Console.WriteLine($"Test finished after {time_sum}seconds.");
            Console.WriteLine($"Average ops/s: {no_reads/time_sum} seconds per read.");
        }
        static void AggregationTest(int n, bool two_weeks){
            var watch = new System.Diagnostics.Stopwatch();
            var end_date = two_weeks ? start_date.AddDays(14):start_date.AddDays(7);
            Console.WriteLine($"Starting aggregation test with {n} querries ({start_date} - {end_date}...)");
            var filter_builder = Builders<Record>.Filter;
            var query = filter_builder.Gte(x => x.timestamp, start_date) & filter_builder.Lte(x => x.timestamp, end_date) & filter_builder.Exists(x => x.UEData);
            double time_sum = 0;
            for (int i = 0; i < n; i++){
                var db = clients[0].GetDatabase("benchmark");
                var collection = db.GetCollection<Record>("metrics");
                watch.Start();
                var res = collection.Aggregate<Record>()
                .Match(query)
                .Group(g => g.UEData.pci, // Group by a constant value (1) or any specific field
                    g => new { avg = g.Average(x => x.UEData.dlul_brate) }).ToBsonDocument();//g.Average(x => x.Field1) });
                watch.Stop();
                Console.WriteLine(res);
                time_sum += watch.Elapsed.TotalSeconds;
            }
            Console.WriteLine($"Test finished after {time_sum}seconds.");
            Console.WriteLine($"Average delay: {time_sum/n} seconds per read.");
        }

    }
}