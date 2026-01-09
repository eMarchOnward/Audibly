// Author: rstewa · https://github.com/rstewa
// Created: 04/15/2024
// Updated: 10/11/2024

using System.IO;
using Audibly.Models;
using Microsoft.EntityFrameworkCore;

namespace Audibly.Repository.Sql;

public class AudiblyContext : DbContext
{
    /// <summary>
    ///     Creates a new Audibly DbContext.
    /// </summary>
    /// <param name="options"></param>
    public AudiblyContext(DbContextOptions<AudiblyContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            
            // Ensure the directory exists
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            
            var dbPath = Path.Combine(folderPath, "Audibly.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // modelBuilder.Entity<Audiobook>()
        //     .HasMany(a => a.SourcePaths)
        //     .WithOne(s => s.Audiobook)
        //     .HasForeignKey(s => s.AudiobookId)
        //     .OnDelete(DeleteBehavior.Cascade);
        //
        // modelBuilder.Entity<Audiobook>()
        //     .HasMany(a => a.Chapters)
        //     .WithOne(c => c.Audiobook)
        //     .HasForeignKey(c => c.AudiobookId)
        //     .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Audiobook>()
            .HasIndex(a => new { a.Author, a.Title })
            .IsUnique();

        modelBuilder.Entity<Tag>()
            .HasIndex(t => t.NormalizedName)
            .IsUnique();

        modelBuilder.Entity<Audiobook>()
            .HasMany<Tag>(a => a.Tags)
            .WithMany(t => t.Audiobooks)
            .UsingEntity(j => j.ToTable("AudiobookTag"));
    }

    /// <summary>
    ///     Gets the audiobooks DbSet.
    /// </summary>
    public DbSet<Audiobook> Audiobooks { get; set; }

    public DbSet<ChapterInfo> Chapters { get; set; }

    public DbSet<SourceFile> SourceFiles { get; set; }

    public DbSet<Bookmark> Bookmarks { get; set; }

    public DbSet<Tag> Tags { get; set; }
}