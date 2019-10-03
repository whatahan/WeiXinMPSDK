using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.IO;

using Senparc.CO2NET;
using Senparc.CO2NET.Cache;
using Senparc.Weixin.RegisterServices;
using Senparc.CO2NET.RegisterServices;
using Senparc.Weixin.Entities;
using Senparc.CO2NET.Cache.Memcached;//DPBMARK Memcached DPBMARK_END
using Senparc.Weixin.Cache.Memcached;//DPBMARK Memcached DPBMARK_END
using Senparc.CO2NET.Cache.Redis;//DPBMARK Redis DPBMARK_END
using Senparc.Weixin.Cache.Redis;//DPBMARK Redis DPBMARK_END
using Senparc.Weixin.Open;//DPBMARK Open DPBMARK_END
using Senparc.Weixin.Open.ComponentAPIs;//DPBMARK Open DPBMARK_END
using Senparc.Weixin.TenPay;//DPBMARK TenPay DPBMARK_END
using Senparc.Weixin.Work;//DPBMARK Work DPBMARK_END
using Senparc.Weixin.WxOpen;//DPBMARK MiniProgram DPBMARK_END
using Senparc.Weixin.MP;//DPBMARK MP DPBMARK_END
using Senparc.WebSocket;//DPBMARK WebSocket DPBMARK_END
using Senparc.Weixin.Sample.NetCore3.WebSocket.Hubs;//DPBMARK WebSocket DPBMARK_END

using Senparc.CO2NET.Utilities;
using Senparc.Weixin.MP.Sample.CommonService.MessageHandlers.WebSocket;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Senparc.CO2NET.Extensions;
using Senparc.Weixin.MP.MessageHandlers.Middleware;
using Senparc.Weixin.MP.Sample.CommonService.CustomMessageHandler;
using System.Text;
using Senparc.NeuChar.MessageHandlers;

namespace Senparc.Weixin.Sample.NetCore3
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSession();//使用Session（实践证明需要在配置 Mvc 之前）

            services.AddControllersWithViews()
                    .AddNewtonsoftJson()// 支持 NewtonsoftJson
                    .SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Version_3_0);
            // Add CookieTempDataProvider after AddMvc and include ViewFeatures.
            services.AddSingleton<ITempDataProvider, CookieTempDataProvider>();

            //如果部署在linux系统上，需要加上下面的配置：
            //services.Configure<KestrelServerOptions>(options => options.AllowSynchronousIO = true);
            //如果部署在IIS上，需要加上下面的配置：
            services.Configure<IISServerOptions>(options => options.AllowSynchronousIO = true);

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddMemoryCache();//使用本地缓存必须添加

            services.AddSignalR();//使用 SignalR   -- DPBMARK WebSocket DPBMARK_END

            /*
             * CO2NET 是从 Senparc.Weixin 分离的底层公共基础模块，经过了长达 6 年的迭代优化，稳定可靠。
             * 关于 CO2NET 在所有项目中的通用设置可参考 CO2NET 的 Sample：
             * https://github.com/Senparc/Senparc.CO2NET/blob/master/Sample/Senparc.CO2NET.Sample.netcore/Startup.cs
             */

            services.AddSenparcGlobalServices(Configuration)//Senparc.CO2NET 全局注册
                    .AddSenparcWeixinServices(Configuration)//Senparc.Weixin 注册
                    .AddSenparcWebSocket<CustomNetCoreWebSocketMessageHandler>();//Senparc.WebSocket 注册（按需）
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env,
                IOptions<SenparcSetting> senparcSetting, IOptions<SenparcWeixinSetting> senparcWeixinSetting)
        {
            //启用 GB2312
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            //引入EnableRequestRewind中间件
            app.UseEnableRequestRewind();
            app.UseSession();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });


            //使用 SignalR（.NET Core 3.0）                                                      -- DPBMARK WebSocket
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<SenparcHub>("/senparcHub");
            });                                                                                  // DPBMARK_END



            // 启动 CO2NET 全局注册，必须！

            //关于 UseSenparcGlobal() 的更多用法见 CO2NET Demo：https://github.com/Senparc/Senparc.CO2NET/blob/master/Sample/Senparc.CO2NET.Sample.netcore3/Startup.cs
            var registerService = app.UseSenparcGlobal(env, senparcSetting.Value, register =>
                     {
                         #region CO2NET 全局配置

                         #region 全局缓存配置（按需）

                         //当同一个分布式缓存同时服务于多个网站（应用程序池）时，可以使用命名空间将其隔离（非必须）
                         register.ChangeDefaultCacheNamespace("DefaultCO2NETCache");

                         #region 配置和使用 Redis          -- DPBMARK Redis

                         //配置全局使用Redis缓存（按需，独立）
                         if (UseRedis(senparcSetting.Value, out string redisConfigurationStr))//这里为了方便不同环境的开发者进行配置，做成了判断的方式，实际开发环境一般是确定的，这里的if条件可以忽略
                         {
                             /* 说明：
                              * 1、Redis 的连接字符串信息会从 Config.SenparcSetting.Cache_Redis_Configuration 自动获取并注册，如不需要修改，下方方法可以忽略
                             /* 2、如需手动修改，可以通过下方 SetConfigurationOption 方法手动设置 Redis 链接信息（仅修改配置，不立即启用）
                              */
                             Senparc.CO2NET.Cache.Redis.Register.SetConfigurationOption(redisConfigurationStr);

                             //以下会立即将全局缓存设置为 Redis
                             Senparc.CO2NET.Cache.Redis.Register.UseKeyValueRedisNow();//键值对缓存策略（推荐）
                                                                                       //Senparc.CO2NET.Cache.Redis.Register.UseHashRedisNow();//HashSet储存格式的缓存策略

                             //也可以通过以下方式自定义当前需要启用的缓存策略
                             //CacheStrategyFactory.RegisterObjectCacheStrategy(() => RedisObjectCacheStrategy.Instance);//键值对
                             //CacheStrategyFactory.RegisterObjectCacheStrategy(() => RedisHashSetObjectCacheStrategy.Instance);//HashSet
                         }
                         //如果这里不进行Redis缓存启用，则目前还是默认使用内存缓存 

                         #endregion                        // DPBMARK_END

                         #region 配置和使用 Memcached      -- DPBMARK Memcached

                         //配置Memcached缓存（按需，独立）
                         if (UseMemcached(senparcSetting.Value, out string memcachedConfigurationStr)) //这里为了方便不同环境的开发者进行配置，做成了判断的方式，实际开发环境一般是确定的，这里的if条件可以忽略
                         {
                             app.UseEnyimMemcached();

                             /* 说明：
                             * 1、Memcached 的连接字符串信息会从 Config.SenparcSetting.Cache_Memcached_Configuration 自动获取并注册，如不需要修改，下方方法可以忽略
                            /* 2、如需手动修改，可以通过下方 SetConfigurationOption 方法手动设置 Memcached 链接信息（仅修改配置，不立即启用）
                             */
                             Senparc.CO2NET.Cache.Memcached.Register.SetConfigurationOption(memcachedConfigurationStr);

                             //以下会立即将全局缓存设置为 Memcached
                             Senparc.CO2NET.Cache.Memcached.Register.UseMemcachedNow();

                             //也可以通过以下方式自定义当前需要启用的缓存策略
                             CacheStrategyFactory.RegisterObjectCacheStrategy(() => MemcachedObjectCacheStrategy.Instance);
                         }

                         #endregion                        //  DPBMARK_END

                         #endregion

                         #region 注册日志（按需，建议）

                         register.RegisterTraceLog(ConfigTraceLog);//配置TraceLog

                         #endregion

                         #region APM 系统运行状态统计记录配置

                         //测试APM缓存过期时间（默认情况下可以不用设置）
                         CO2NET.APM.Config.EnableAPM = true;//默认已经为开启，如果需要关闭，则设置为 false
                         CO2NET.APM.Config.DataExpire = TimeSpan.FromMinutes(60);

                         #endregion

                         #endregion
                     },

                     #region 扫描自定义扩展缓存

                       //自动扫描自定义扩展缓存（二选一）
                       autoScanExtensionCacheStrategies: true //默认为 true，可以不传入
                                                              //指定自定义扩展缓存（二选一）
                                                              //autoScanExtensionCacheStrategies: false, extensionCacheStrategiesFunc: () => GetExCacheStrategies(senparcSetting.Value)

            #endregion

                   )
                   //使用 Senparc.Weixin SDK
                   .UseSenparcWeixin(senparcWeixinSetting.Value, register =>
                   {
                       #region 微信相关配置

                       /* 微信配置开始
                        * 
                        * 建议按照以下顺序进行注册，尤其须将缓存放在第一位！
                        */

                       //注册开始

                       #region 微信缓存（按需，必须放在配置开头，以确保其他可能依赖到缓存的注册过程使用正确的配置）

                       //微信的 Redis 缓存，如果不使用则注释掉（开启前必须保证配置有效，否则会抛错）         -- DPBMARK Redis
                       if (UseRedis(senparcSetting.Value, out _))
                       {
                           app.UseSenparcWeixinCacheRedis();
                       }                                                                                     // DPBMARK_END

                       // 微信的 Memcached 缓存，如果不使用则注释掉（开启前必须保证配置有效，否则会抛错）    -- DPBMARK Memcached
                       if (UseMemcached(senparcSetting.Value, out _))
                       {
                           app.UseSenparcWeixinCacheMemcached();
                       }                                                                                      // DPBMARK_END

                       #endregion

                       #region 注册公众号或小程序（按需）

                       //注册公众号（可注册多个）                                                    -- DPBMARK MP
                       register
                              .RegisterMpAccount(senparcWeixinSetting.Value, "【盛派网络小助手】公众号")// DPBMARK_END


                              //注册多个公众号或小程序（可注册多个）                                        -- DPBMARK MiniProgram
                              .RegisterWxOpenAccount(senparcWeixinSetting.Value, "【盛派网络小助手】小程序")// DPBMARK_END

                              //除此以外，仍然可以在程序任意地方注册公众号或小程序：
                              //AccessTokenContainer.Register(appId, appSecret, name);//命名空间：Senparc.Weixin.MP.Containers
                       #endregion

                       #region 注册企业号（按需）           -- DPBMARK Work

                              //注册企业微信（可注册多个）
                              .RegisterWorkAccount(senparcWeixinSetting.Value, "【盛派网络】企业微信")

                              //除此以外，仍然可以在程序任意地方注册企业微信：
                              //AccessTokenContainer.Register(corpId, corpSecret, name);//命名空间：Senparc.Weixin.Work.Containers
                       #endregion                          // DPBMARK_END

                       #region 注册微信支付（按需）        -- DPBMARK TenPay

                              //注册旧微信支付版本（V2）（可注册多个）
                              .RegisterTenpayOld(senparcWeixinSetting.Value, "【盛派网络小助手】公众号")//这里的 name 和第一个 RegisterMpAccount() 中的一致，会被记录到同一个 SenparcWeixinSettingItem 对象中

                              //注册最新微信支付版本（V3）（可注册多个）
                              .RegisterTenpayV3(senparcWeixinSetting.Value, "【盛派网络小助手】公众号")//记录到同一个 SenparcWeixinSettingItem 对象中

                       #endregion                          // DPBMARK_END

                       #region 注册微信第三方平台（按需）  -- DPBMARK Open

                              //注册第三方平台（可注册多个）
                              .RegisterOpenComponent(senparcWeixinSetting.Value,
                                 //getComponentVerifyTicketFunc
                                 async componentAppId =>
                                 {
                                     var dir = Path.Combine(ServerUtility.ContentRootMapPath("~/App_Data/OpenTicket"));
                                     if (!Directory.Exists(dir))
                                     {
                                         Directory.CreateDirectory(dir);
                                     }

                                     var file = Path.Combine(dir, string.Format("{0}.txt", componentAppId));
                                     using (var fs = new FileStream(file, FileMode.Open))
                                     {
                                         using (var sr = new StreamReader(fs))
                                         {
                                             var ticket = await sr.ReadToEndAsync();
                                             return ticket;
                                         }
                                     }
                                 },

                                //getAuthorizerRefreshTokenFunc
                                async (componentAppId, auhtorizerId) =>
                                {
                                    var dir = Path.Combine(ServerUtility.ContentRootMapPath("~/App_Data/AuthorizerInfo/" + componentAppId));
                                    if (!Directory.Exists(dir))
                                    {
                                        Directory.CreateDirectory(dir);
                                    }

                                    var file = Path.Combine(dir, string.Format("{0}.bin", auhtorizerId));
                                    if (!File.Exists(file))
                                    {
                                        return null;
                                    }

                                    using (Stream fs = new FileStream(file, FileMode.Open))
                                    {
                                        var binFormat = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                                        var result = (RefreshAuthorizerTokenResult)binFormat.Deserialize(fs);
                                        return result.authorizer_refresh_token;
                                    }
                                },

                                  //authorizerTokenRefreshedFunc
                                  (componentAppId, auhtorizerId, refreshResult) =>
                                  {
                                      var dir = Path.Combine(ServerUtility.ContentRootMapPath("~/App_Data/AuthorizerInfo/" + componentAppId));
                                      if (!Directory.Exists(dir))
                                      {
                                          Directory.CreateDirectory(dir);
                                      }

                                      var file = Path.Combine(dir, string.Format("{0}.bin", auhtorizerId));
                                      using (Stream fs = new FileStream(file, FileMode.Create))
                                      {
                                          //这里存了整个对象，实际上只存RefreshToken也可以，有了RefreshToken就能刷新到最新的AccessToken
                                          var binFormat = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                                          binFormat.Serialize(fs, refreshResult);
                                          fs.Flush();
                                      }
                                  }, "【盛派网络】开放平台")

                          //除此以外，仍然可以在程序任意地方注册开放平台：
                          //ComponentContainer.Register();//命名空间：Senparc.Weixin.Open.Containers
                       #endregion                          // DPBMARK_END

                          ;

                       /* 微信配置结束 */

                       #endregion
                   });

            //使用公众号的 MessageHandler 中间件                                                 -- DPBMARK MP
            app.UseMessageHandler("/Weixin2", CustomMessageHandler.GenerateMessageHandler, options =>
            {
                options.DefaultMessageHandlerAsyncEvent = DefaultMessageHandlerAsyncEvent.SelfSynicMethod;//异步方法不存在时调用同步方法
                //配置 SenparcWeixinSetting 参数，以自动提供 Token、EncodingAESKey 等参数。
                //此处为委托，可以根据条件动态判断输入条件
                options.SenparcWeixinSetting = context => Config.SenparcWeixinSetting["【盛派网络小助手】公众号"];//或：senparcWeixinSetting.Value;
            });                                                                                   // DPBMARK_END


        }


        /// <summary>
        /// 配置微信跟踪日志
        /// </summary>
        private void ConfigTraceLog()
        {
            //这里设为Debug状态时，/App_Data/WeixinTraceLog/目录下会生成日志文件记录所有的API请求日志，正式发布版本建议关闭

            //如果全局的IsDebug（Senparc.CO2NET.Config.IsDebug）为false，此处可以单独设置true，否则自动为true
            CO2NET.Trace.SenparcTrace.SendCustomLog("系统日志", "系统启动");//只在Senparc.Weixin.Config.IsDebug = true的情况下生效

            //全局自定义日志记录回调
            CO2NET.Trace.SenparcTrace.OnLogFunc = () =>
            {
                //加入每次触发Log后需要执行的代码
            };

            //当发生基于WeixinException的异常时触发
            WeixinTrace.OnWeixinExceptionFunc = ex =>
            {
                //加入每次触发WeixinExceptionLog后需要执行的代码

                //发送模板消息给管理员                             -- DPBMARK Redis
                var eventService = new Senparc.Weixin.MP.Sample.CommonService.EventService();
                eventService.ConfigOnWeixinExceptionFunc(ex);      // DPBMARK_END
            };
        }

        /// <summary>
        /// 判断当前配置是否满足使用 Redis（根据是否已经修改了默认配置字符串判断）
        /// </summary>
        /// <param name="senparcSetting"></param>
        /// <returns></returns>
        private bool UseRedis(SenparcSetting senparcSetting, out string redisConfigurationStr)
        {
            redisConfigurationStr = senparcSetting.Cache_Redis_Configuration;
            var useRedis = !string.IsNullOrEmpty(redisConfigurationStr) && redisConfigurationStr != "#{Cache_Redis_Configuration}#"/*默认值，不启用*/;
            return useRedis;
        }

        /// <summary>
        /// 初步判断当前配置是否满足使用 Memcached（根据是否已经修改了默认配置字符串判断）
        /// </summary>
        /// <param name="senparcSetting"></param>
        /// <returns></returns>
        private bool UseMemcached(SenparcSetting senparcSetting, out string memcachedConfigurationStr)
        {
            memcachedConfigurationStr = senparcSetting.Cache_Memcached_Configuration;
            var useMemcached = !string.IsNullOrEmpty(memcachedConfigurationStr) && memcachedConfigurationStr != "#{Cache_Memcached_Configuration}#";
            return useMemcached;
        }
    }
}
