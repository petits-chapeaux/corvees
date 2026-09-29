using Microsoft.EntityFrameworkCore;

namespace Corvees.Infrastructure;

public sealed class CorveesDbContext(DbContextOptions<CorveesDbContext> options) : DbContext(options);
