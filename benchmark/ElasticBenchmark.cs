using System.Diagnostics;
using System.Linq.Expressions;
using Elasticsearch.Net;
using Models;
using Nest;

public class ElasticBenchmark : IDatabaseBenchmark{

    private ElasticClient client;
    private DateTime startTime;
    private StreamWriter outSW;
    private bool writeToFile;
    public ElasticBenchmark(Uri clientUri, DateTime _startTime, bool writeToFile = false){
        this.startTime = _startTime;
	    var clientSettings = new ConnectionSettings(clientUri).DefaultIndex("metrics");
        this.client = new ElasticClient(clientSettings);
        this.writeToFile = writeToFile;
        if(writeToFile){
            outSW = File.AppendText("elastic_standalone_agg.txt");
            outSW.AutoFlush = true;
        }
    }

    public ElasticBenchmark(IEnumerable<Uri> nodeUris, DateTime _startTime, bool writeToFile = false){
        this.startTime = _startTime;
        var pool = new StaticConnectionPool(nodeUris);
        var clientSettings = new ConnectionSettings(pool).DefaultIndex("metrics");
        
        client = new ElasticClient(clientSettings);
    	this.writeToFile = writeToFile;
	if(writeToFile){
		outSW = File.AppendText("elastic_cluster_agg.txt");
		outSW.AutoFlush = true;
	}
    }
    ~ElasticBenchmark(){
    	outSW.Close();
    } 
    public void SetupDB(){
        this.client.Indices.Create("metrics", 
            c => 
                c.Map<UEData>(m => m.AutoMap())
                .Map<Record>(m => m.AutoMap()));
    }
    public void ResetDB(){
        this.client.Indices.Delete("metrics");
    }
   
    public void SequentialWriteTest(int timePointsCount, int dt){
    	Console.WriteLine($"Write test: {timePointsCount*15} records...");
        if(writeToFile) outSW.WriteLine($"Write test: {timePointsCount*15} records...");
        BenchmarkDataGenerator gen = new BenchmarkDataGenerator(-1, timePointsCount, startTime, dt);
        var watch = new Stopwatch();
        int recordNumber = 1;
        foreach (var chunk in gen.GetTimepointChunk()){
            if(recordNumber%1_000 == 0 && recordNumber != 0) Console.WriteLine($"{100.0*recordNumber/timePointsCount:0.00}%");
            watch.Start();
            this.client.IndexMany(chunk);
            watch.Stop();
            recordNumber += 1;
        }
        
       	outSW.WriteLine($"Write test of {timePointsCount*15} records finished.");
        outSW.WriteLine($"Total time only inserting data: {watch.Elapsed.TotalSeconds}\n");
        Console.WriteLine($"Write test finished after {watch.Elapsed.TotalSeconds} seconds");
    }
    public void BulkLoad(int timePointsCount, int chunkSize){
        Console.WriteLine($"Bulk loading {timePointsCount*18} records in chunks of {chunkSize*18}...");
        BenchmarkDataGenerator gen = new BenchmarkDataGenerator(chunkSize, timePointsCount, startTime,1);
        int chunkNumber = 1;
        Stopwatch watch1 = new Stopwatch();
        Stopwatch watch2 = new Stopwatch();
        watch1.Start();
        foreach (var data in gen.GetDataChunk()){
            Console.WriteLine($"{100.0*chunkNumber/gen.chunkCount:0.00}%");
            foreach(var r in data){
                watch2.Start();
                var t = this.client.IndexDocument(r);
                watch2.Stop();
            }
            chunkNumber += 1;
        }
        watch1.Stop();
        outSW.WriteLine($"Bulk load {timePointsCount*18} records, chunks of {chunkSize*18}\n");
        outSW.WriteLine($"Total time including generating data: {watch1.Elapsed.TotalSeconds}");
        outSW.WriteLine($"Total time only inserting data: {watch2.Elapsed.TotalSeconds}\n");
        Console.WriteLine("Bulk loading finished.\n");
    }
    public void SequentialReadTest(int readCount){
        Console.WriteLine($"Starting sequential read test for {readCount} reads...");
       	Stopwatch watch = new System.Diagnostics.Stopwatch();
        var searchRequests = new List<Task<ISearchResponse<Record>>>();
        watch.Start();
        for (int i = 0; i < readCount; i++){
            var request = new SearchRequest<Record>(){
                Query = new TermQuery(){
                    Field = Infer.Field<Record>(r => r.ue_data.ue_id),
                    Value = new Random().Next(15)
                },
                From = 0,
		        Size = 1
            };
            if(i%10_000 == 0 && i != 0) Console.WriteLine(i);
            searchRequests.Add(this.client.SearchAsync<Record>(request));
        }

        var tasks = Task.WhenAll(searchRequests);
        tasks.Wait();
        watch.Stop();
	

        Console.WriteLine("Sequential read test finished.");
        Console.WriteLine(watch.Elapsed.TotalSeconds);
	if(this.writeToFile){
            outSW.WriteLine($"Sequential read test: {readCount} reads.");
            outSW.WriteLine($"Total time for {readCount} reads: {watch.Elapsed.TotalSeconds} seconds.");
            outSW.WriteLine($"Ops/second: {readCount / watch.Elapsed.TotalSeconds}.\n");
        }
    }
    public void AggregationTest(int queryCount){
        var watch = new System.Diagnostics.Stopwatch();
        var endTimeOneWeek = startTime.AddDays(7);
        var endTimeTwoWeeks = startTime.AddDays(14);
        Console.WriteLine($"Aggregation test: {queryCount} queries, one week");
        Console.WriteLine($"Starting aggregation test with {queryCount} queries ({startTime} - {endTimeOneWeek}...)");
        if(this.writeToFile) outSW.WriteLine($"Aggregation test: {queryCount} queries, one week");
        if(this.writeToFile) outSW.WriteLine($"Starting aggregation test with {queryCount} queries ({startTime} - {endTimeOneWeek}...)");
        
	for (int i = 0; i < queryCount; i++){
            var query = new SearchRequest<Record>(){
                Query = new DateRangeQuery{
                    Field = Infer.Field<Record>(r => r.timestamp),
                    GreaterThanOrEqualTo = startTime.ToUniversalTime(),
                    LessThanOrEqualTo = endTimeOneWeek.ToUniversalTime()
                },
		 Aggregations =new AverageAggregation("avg", Infer.Field<Record>(r => r.ue_data.dl_brate)),
		 Size = 0,
		 TrackTotalHits = true
		
            };
            
            watch.Start();
            var res = this.client.Search<Record>(query);
            watch.Stop();

	    if(i%1_000 == 0 && i != 0) Console.WriteLine($"Finished {i} queries...");
        }

        Console.WriteLine("Aggregation test finished.");
        Console.WriteLine($"Total time for {queryCount} queries: {watch.Elapsed.TotalSeconds} seconds.");
        Console.WriteLine($"Ops/second: {queryCount / watch.Elapsed.TotalSeconds}.\n");
        Console.WriteLine($"Starting aggregation test with {queryCount} querries ({startTime} - {endTimeTwoWeeks}...)");
       	if(this.writeToFile){
		outSW.WriteLine("Aggregation test finished.");
        	outSW.WriteLine($"Total time for {queryCount} queries: {watch.Elapsed.TotalSeconds} seconds.");
        	outSW.WriteLine($"Ops/second: {queryCount / watch.Elapsed.TotalSeconds}.\n");
        	outSW.WriteLine($"Starting aggregation test with {queryCount} querries ({startTime} - {endTimeTwoWeeks}...)");
	}
	watch.Reset();
    for (int i = 0; i < queryCount; i++){
        var query = new SearchRequest<Record>(){
            Query = new DateRangeQuery{
                Field = Infer.Field<Record>(r => r.timestamp),
                GreaterThanOrEqualTo = startTime.ToUniversalTime(),
                LessThanOrEqualTo = endTimeTwoWeeks.ToUniversalTime()
            },
        Aggregations =new AverageAggregation("avg", Infer.Field<Record>(r => r.ue_data.dl_brate)),
        Size = 0,
        TrackTotalHits = true
    
        };
        
        watch.Start();
        var res = this.client.Search<Record>(query);
        watch.Stop();
        Console.WriteLine($"AVG: {res.Aggregations.Average("avg").Value}");
        Console.WriteLine($"Total: {res.Total}");
        Console.WriteLine($"Took: {res.Took}");

        outSW.WriteLine($"AVG: {res.Aggregations.Average("avg").Value}");
        outSW.WriteLine($"Total: {res.Total}");
        outSW.WriteLine($"Took: {res.Took}");
        if(i%1_000 == 0 && i != 0) Console.WriteLine($"Finished {i} queries...");
    }           
        Console.WriteLine("Aggregation test finished.");
        Console.WriteLine($"Total time for {queryCount} queries: {watch.Elapsed.TotalSeconds} seconds.");
        Console.WriteLine($"Ops/second: {queryCount / watch.Elapsed.TotalSeconds}.\n");
        if(this.writeToFile){
		outSW.WriteLine("Aggregation test finished.");
        	outSW.WriteLine($"Total time for {queryCount} queries: {watch.Elapsed.TotalSeconds} seconds.");
        	outSW.WriteLine($"Ops/second: {queryCount / watch.Elapsed.TotalSeconds}.\n");
	    }

    }

}