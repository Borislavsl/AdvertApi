using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Amazon.Extensions.NETCore.Setup;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using AutoMapper;
using AdvertApi.Models.BS;

namespace AdvertApi.Services
{
    public class DynamoDBAdvertStorage : IAdvertStorageService
    {
        public IConfiguration Configuration { get; set; }

        private const string TABLE_NAME = "Adverts";
        private readonly IMapper _mapper;

        public DynamoDBAdvertStorage(IMapper mapper)
        {            
            _mapper = mapper;
        }

        public async Task<string> AddAsync(AdvertModel model)
        {
            AdvertDbModel dbModel = _mapper.Map<AdvertDbModel>(model);

            dbModel.Id = Guid.NewGuid().ToString();
            dbModel.CreationDateTime = DateTime.UtcNow;
            dbModel.Status = AdvertStatus.Pending;

            using (AmazonDynamoDBClient client = GetDynamoDBClient())
            {
                DescribeTableResponse table = await client.DescribeTableAsync(TABLE_NAME);

                using (var context = new DynamoDBContext(client))
                {
                    await context.SaveAsync(dbModel);
                }
            }

            return dbModel.Id;
        }

        private AmazonDynamoDBClient GetDynamoDBClient()
        {
            AWSOptions options = Configuration.GetAWSOptions();

            return new AmazonDynamoDBClient(options.Region);
        }

        public async Task<bool> CheckHealthAsync()
        {
            Console.WriteLine("Health checking...");
            using (AmazonDynamoDBClient client = GetDynamoDBClient())
            {
                DescribeTableResponse tableData = await client.DescribeTableAsync(TABLE_NAME);
                return string.Compare(tableData.Table.TableStatus, "active", true) == 0;
            }
        }

        public async Task ConfirmAsync(ConfirmAdvertModel model)
        {
            using (AmazonDynamoDBClient client = GetDynamoDBClient())
            {
                using (var context = new DynamoDBContext(client))
                {
                    var record = await context.LoadAsync<AdvertDbModel>(model.Id);
                    if (record == null)
                        throw new KeyNotFoundException($"A record with ID={model.Id} was not found.");

                    if (model.Status == AdvertStatus.Active)
                    {
                        record.FilePath = model.FilePath;
                        record.Status = AdvertStatus.Active;
                        await context.SaveAsync(record);
                    }
                    else
                    {
                        await context.DeleteAsync(record);
                    }
                }
            }
        }

        public async Task<List<AdvertModel>> GetAllAsync()
        {
            using (AmazonDynamoDBClient client = GetDynamoDBClient())
            {
                using (var context = new DynamoDBContext(client))
                {
                    var scanResult = await context.ScanAsync<AdvertDbModel>(new List<ScanCondition>()).GetNextSetAsync();
                    return scanResult.Select(item => _mapper.Map<AdvertModel>(item)).ToList();
                }
            }
        }

        public async Task<AdvertModel> GetByIdAsync(string id)
        {
            using (AmazonDynamoDBClient client = GetDynamoDBClient())
            {
                using (var context = new DynamoDBContext(client))
                {
                    AdvertDbModel dbModel = await context.LoadAsync<AdvertDbModel>(id);
                    if (dbModel != null)
                        return _mapper.Map<AdvertModel>(dbModel);
                }
            }

            throw new KeyNotFoundException();
        }
    }
}