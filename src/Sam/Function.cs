using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System.Text.Json;


[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
namespace Sam;

public class Function
{
    private readonly DynamoDBContext _dbContext;
    private readonly AmazonSimpleNotificationServiceClient _snsClient;

    private readonly string? _tasksSns;


    public Function()
    {
        var dynamoClient = new AmazonDynamoDBClient();
        _dbContext = new DynamoDBContext(dynamoClient);
        _snsClient = new AmazonSimpleNotificationServiceClient();
        _tasksSns = Environment.GetEnvironmentVariable("TASKS_SNS");
        
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> RegisterTask(APIGatewayProxyRequest input,
        ILambdaContext context)
    {
        var request = JsonSerializer.Deserialize<RegisterTaskRequest>(input.Body, new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true
        })!;

        var task = new TaskModel() { TaskId = Guid.NewGuid(), Name = request.Name };
        await _dbContext.SaveAsync(task);

        var @event = new PublishRequest
        {
            TopicArn = _tasksSns,
            Message = JsonSerializer.Serialize(new TaskRegistered(task.TaskId, task.Name))
        };
        
        await _snsClient.PublishAsync(@event);

        return new APIGatewayHttpApiV2ProxyResponse
        {
            Body = JsonSerializer.Serialize(new RegisterTaskResponse(task.TaskId)),
            StatusCode = 200,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        };
    }

    [DynamoDBTable("tasks")]
    public class TaskModel
    {
        [DynamoDBHashKey("taskid")]
        public Guid TaskId { get; set; }
        
        [DynamoDBProperty("name")]
        public string? Name { get; set; }
    }

    [DynamoDBTable("assignments")]
    public class AssignmentModel
    {
        [DynamoDBProperty("assignmentid")]
        public Guid AssignmentId { get; set; }
        
        [DynamoDBHashKey("taskid")]
        public Guid TaskId { get; set; }
        
        [DynamoDBProperty("worker")]
        public string? Worker { get; set; }
    }

    public class SNSWrapper
    {
        public string Messsage { get; set; } = null!;
    }
    
    public record AssignmentDTO(Guid AssignmentId, string TaskId, string? Worker);

    public record TaskDTO(Guid TaskId, string? Name);

    public record TaskRegistered(Guid TaskId, string? Name);

    public class RegisterTaskRequest
    {
        public string? Name { get; set; }
    }

    public record RegisterTaskResponse(Guid TaskId);
}
