namespace ReactAPI 
{ 
    public class Program 
    { 
        public static void Main(string[] args) 
        { 
            
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("ReactPolicy",
                    policy =>
                    {
                        policy.WithOrigins(
                            "http://localhost:3000",
                            "https://simonrnielsen.github.io"
                        )
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                    });
            });

            // Add services to the container.
            builder.Services.AddControllers(); 
            
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi(); 
            
            var app = builder.Build();

            app.UseCors("ReactPolicy");

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment()) 
            { 
                app.MapOpenApi(); 
            } 
            
            app.UseHttpsRedirection(); 

            app.UseAuthorization(); 
            app.MapControllers();

            app.Run(); 

        } 
    } 
}