using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Confluent.Kafka;

namespace api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        private readonly Producer<Null, string> _producer;

        public ValuesController(Producer<Null, string> producer) {
            _producer = producer;
        }

        [HttpPost("{entry}")]
        public async Task<ActionResult<string>> CreateEntry(string entry) {
            await _producer.ProduceAsync("hello", new Message<Null, string>{ Value = entry });
            Console.WriteLine($"Produced {entry}");

            return $"{entry} published.";
        }
    }
}
