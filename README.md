# lambda sam

- testing SAM for local integration tests for AWS lambda + API Gateway apps

```
aws-vault exec <your_user> --duration 3h
aws lambda list-functions --endpoint-url=http://localhost:4566
aws dynamodb list-tables --endpoint-url=http://localhost:4566
aws sqs list-queues --endpoint-url=http://localhost:4566
aws sns list-topics --endpoint-url=http://localhost:4566
aws apigateway get-rest-apis --endpoint-url=http://localhost:4566

```
