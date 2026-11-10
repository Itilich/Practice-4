using Tic_tac_toe.Hubs;
using Tic_tac_toe.Data;
using Microsoft.EntityFrameworkCore;

namespace Tic_tac_toe
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            builder.Services.AddSignalR();
            builder.Services.AddDbContext<GameDbContext>(options =>
                options.UseSqlite(builder.Configuration.GetConnectionString("GameDatabase") ?? "Data Source=tictactoe.db"));

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
                db.Database.EnsureCreated();
            }

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.MapHub<GameHub>("/game");

            app.Run();
        }
    }
}
