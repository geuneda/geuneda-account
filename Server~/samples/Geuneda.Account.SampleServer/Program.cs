using System;
using System.Security.Cryptography;
using Geuneda.Account.SampleServer;
using Geuneda.Account.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

// 실행: dotnet run --project Server~/samples/Geuneda.Account.SampleServer --no-launch-profile -- --urls http://127.0.0.1:5299
// Unity 상호 운용 테스트: GEUNEDA_ACCOUNT_INTEROP_URL=http://127.0.0.1:5299
// access 토큰 수명과 시계 오차는 --Account:AccessTokenSeconds=5 --Account:ClockSkewSeconds=0 처럼 바꿀 수 있다
var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Account");

// 샘플은 시작할 때마다 새 키를 만든다(다시 시작하면 이전 토큰은 무효). 운영 키는 비밀 저장소에서 읽는다
var signingKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));

builder.Services.AddGeunedaAccount(options =>
	{
		options.Issuer = "geuneda-sample-auth";
		options.Audience = "geuneda-sample-game";
		options.SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
		options.AccessTokenLifetime = TimeSpan.FromSeconds(settings.GetValue("AccessTokenSeconds", 3600));
		options.ClockSkew = TimeSpan.FromSeconds(settings.GetValue("ClockSkewSeconds", 60));
	})
	.AddGuest()
	.AddPlatformVerifier<SamplePlatformVerifier>()
	.AddInMemoryStores();

var app = builder.Build();
app.MapGeunedaAccount("/auth");
app.MapGet("/game/me", SampleGameApi.GetMeAsync);
app.Run();
