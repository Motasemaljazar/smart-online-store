using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace AdminDashboard.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var cs = config.GetConnectionString("DefaultConnection")
            ?? "Server=127.0.0.1;Port=3306;Database=ef_design;User=root;Password=;";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 21));
        optionsBuilder.UseMySql(cs, serverVersion);

        return new AppDbContext(optionsBuilder.Options);
    }
}
