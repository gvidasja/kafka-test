using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace api
{
    public class Startup
    {
        private Task _task;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            Console.WriteLine(Configuration["KAFKA_PRODUCER_CONNECT"]);
            Console.WriteLine(Configuration["KAFKA_CONSUMER_CONNECT"]);

            services.AddSingleton(CreateProducer());
            services.AddSingleton(CreateConsumer());

            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
        }

        private Consumer<Ignore, string> CreateConsumer()
        {
            var consumerConfig = new ConsumerConfig
            {
                EnableAutoCommit = false,
                EnablePartitionEof = true,
                BootstrapServers = Configuration["KAFKA_CONSUMER_CONNECT"],
                GroupId = "group1",
                AutoOffsetReset = AutoOffsetReset.Earliest
            };

            var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig)
                .SetErrorHandler((_, e) => Console.WriteLine($"Error: {e.Reason}"))
                .Build();
            return consumer;
        }

        private Producer<Null, string> CreateProducer()
        {
            var producerConfig = new ProducerConfig
            {
                BootstrapServers = Configuration["KAFKA_PRODUCER_CONNECT"]
            };

            var producer = new ProducerBuilder<Null, string>(producerConfig).Build();
            return producer;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IApplicationLifetime lifetime, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseMvc();

            void OnStart() {
                var consumer = app.ApplicationServices.GetService<Consumer<Ignore, string>>();
                var producer = app.ApplicationServices.GetService<Producer<Null, string>>();

                // consumer.Assign(new []{"hello"}.Select(topic => new TopicPartitionOffset(topic, 0, Offset.Beginning)).ToList());

                consumer.Subscribe(new [] { "hello", "hello_batch" });

                CancellationTokenSource cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => {
                    e.Cancel = true; // prevent the process from terminating.
                    cts.Cancel();
                };

                var cache = new ConcurrentQueue<string>();

                var handlers = new Dictionary<string, Action<string>> {
                    { "hello", message => cache.Enqueue(message) }
                };

                Task.Run(() => {
                    while(true) {
                        var cr = consumer.Consume(cts.Token);

                        if (cr.IsPartitionEOF) {
                            Console.WriteLine("eof");
                            continue;
                        }

                        Console.WriteLine($"consumed {cr.Value}");

                        if (handlers.FirstOrDefault(x => x.Key == cr.Topic).Value is Action<string> handler) {
                            Console.WriteLine($"handling {cr.Topic}");
                            handler(cr.Value);
                        } else {
                            Console.WriteLine($"no handler for {cr.Topic}");
                        }
                    }
                }, cts.Token);

                var timer = new Timer(_ => {
                    var messages = new List<string>();
                    Console.WriteLine(cache.Count);
                    while(cache.TryDequeue(out var message)) {
                        Console.WriteLine($"batching {message}");
                        messages.Add(message);
                    }

                    if (messages.Count == 0) {
                        Console.WriteLine("nothing to batch");
                        return;
                    }

                    producer.ProduceAsync("hello_batch", new Message<Null, string>{ Value = string.Join(",", messages)});
                }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

                Console.WriteLine("SUBSCRIBED");
            }

            void OnStop() {
                var producer = app.ApplicationServices.GetService<Producer<Null, string>>();
                var consumer = app.ApplicationServices.GetService<Consumer<Ignore, string>>();

                consumer.Close();
                producer.Flush(TimeSpan.FromSeconds(2));
            }

            lifetime.ApplicationStarted.Register(OnStart);
            lifetime.ApplicationStopped.Register(OnStop);
        }
    }
}
