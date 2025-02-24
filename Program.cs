using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Fast.Components.FluentUI;
using Microsoft.PowerPlatform.Dataverse.Client;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<DataverseService>();

builder.Services.AddRazorPages();


builder.Services.AddFluentUIComponents();


var app = builder.Build();



app.MapControllerRoute(
    name: "default",
  pattern: "{controller=Solutions}/{action=Index}/{id?}");

app.Run();
