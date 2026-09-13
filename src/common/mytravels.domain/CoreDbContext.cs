using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using mytravels.contract.Dtos;
using mytravels.contract.Entities;
using mytravels.contract.Constants;
using mytravels.contract.Interfaces;
using mytravels.contract.Responses;
using mytravels.domain.Features.PointOfInterest;
using Tag = mytravels.contract.Entities.Tag;

namespace mytravels.domain
{
    public class CoreDbContext : DbContext, ICoreDbContext
    {
        public CoreDbContext(DbContextOptions<CoreDbContext> options) : base(options)
        {
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        public DbSet<PointOfInterest> PointOfInterests { get; set; }
        public DbSet<PointOfInterestTagAssociation> PointOfInterestTagAssociations { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<GetPointOfInterestResponse> GetPointOfInterestResponses { get; set; }
        public DbSet<MessageAuditLog> MessageAuditLogs { get; set; }
        public void DetachObject(object entity) => Entry(entity).State = EntityState.Detached;
        public void DeleteObject(object entity) => Entry(entity).State = EntityState.Deleted;
        public void AddObject(object entity) => Entry(entity).State = EntityState.Added;

        public async Task ExecuteSqlInterpolatedAsync(FormattableString sql, CancellationToken cancellationToken)
            => await this.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);

        public async Task<List<PointOfInterest>> GetPointsOfInterestAsync(CancellationToken cancellationToken)
            => await this.PointOfInterests.ToListAsync(cancellationToken);

        public async Task<List<GetPointOfInterestResponse>> GetPointsOfInterestByKeyAsync(string pointOfInterestKey, CancellationToken cancellationToken)
            => await ExecuteProcInterpolatedAsync<GetPointOfInterestResponse>($"SELECT * FROM public.spGetPointOfInterestById({pointOfInterestKey})");

        public async Task<List<GetPointOfInterestResponse>> GetAllPointsOfInterestAsync(CancellationToken cancellationToken)
            => await ExecuteProcRawAsync<GetPointOfInterestResponse>("SELECT * FROM public.spGetPointOfInterest()");

        public async Task<List<CorrelationSummaryDto>> GetCorrelationSummariesAsync(int page, int pageSize, CancellationToken cancellationToken)
        {
            return await this.MessageAuditLogs
                .GroupBy(x => x.CorrelationId)
                .Select(g => new CorrelationSummaryDto
                {
                    CorrelationId = g.Key,
                    StartedAt = g.Min(x => x.CreatedAt),
                    LastEventAt = g.Max(x => x.CreatedAt),
                    EventCount = g.Count(),
                    HasFailure = g.Any(x => x.EventType == MessageAuditEventTypes.Failed)
                })
                .OrderByDescending(x => x.LastEventAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<MessageAuditLogDto>> GetEventsByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken)
        {
            return await this.MessageAuditLogs
                .Where(x => x.CorrelationId == correlationId)
                .OrderBy(x => x.CreatedAt)
                .Select(x => new MessageAuditLogDto
                {
                    CorrelationId = x.CorrelationId,
                    ExchangeName = x.ExchangeName,
                    EventType = x.EventType,
                    PointOfInterestId = x.PointOfInterestId,
                    RetryCount = x.RetryCount,
                    ErrorMessage = x.ErrorMessage,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<int> UpdatePointOfInterestTagsAsync(List<SavePointOfInterestDto> dtos, CancellationToken cancellationToken)
        {
            if (dtos.Count == 0) return 0;

            List<PointOfInterestTag> tags = new();
            foreach (var d in dtos)
            {
                foreach (string tagName in d.Tags)
                {
                    tags.Add(new PointOfInterestTag
                    {
                        PointOfInterestId = d.PointOfInterestId,
                        TagName = tagName
                    });
                }
            }

            string p_pointOfInterestTagType = JsonSerializer.Serialize(tags);
            int rowsAffected = await this.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT * FROM public.spUpdatePointOfInterestTags({p_pointOfInterestTagType}::json)", cancellationToken);
            return rowsAffected;
        }

        public async Task<int> CreatePointOfInterestAsync(PointOfInterest point, CancellationToken cancellationToken)
        {
            this.AddObject(point);
            await this.SaveChangesAsync(cancellationToken);
            return point.Id;
        }

        public async Task AddImageToPointOfInterestAsync(string blobName, PointOfInterest point, CancellationToken cancellationToken)
        {
            point.Id = 0;
            point.Container = BucketNames.NewUploadedImagesContainer;
            point.DateCreated = DateTime.UtcNow;
            point.GeneratedBlobName = blobName;
            point.OriginalFileName = blobName;

            // The entity is re-inserted rather than built fresh, so flags from the superseded row would
            // otherwise carry over. ImageResized must start false or ResizeImage skips the new blob and
            // nothing is ever written to resized-images under this GeneratedBlobName.
            point.ImageResized = false;
            this.AddObject(point);
            await this.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateAddressAsync(UpdateAddressDto dto, CancellationToken cancellationToken)
        {
            List<PointOfInterest> points = await this.GetPointsOfInterestAsync(cancellationToken);
            points = points.Where(x => x.PointOfInterestKey == dto.PointOfInterestKey).ToList();
            foreach (var point in points)
            {
                point.DateUpdated = DateTime.UtcNow;
                point.Latitude = dto.Latitude;
                point.Longitude = dto.Longitude;
                point.FormattedAddress = dto.FormattedAddress;
                var entry = this.Entry(point);
                entry.State = EntityState.Unchanged;
                entry.Property(nameof(point.Latitude)).IsModified = true;
                entry.Property(nameof(point.Longitude)).IsModified = true;
                entry.Property(nameof(point.FormattedAddress)).IsModified = true;
            }
            await this.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<GetPointOfInterestResponse>().ToTable(nameof(GetPointOfInterestResponse), t => t.ExcludeFromMigrations());

            modelBuilder.Entity<Tag>()
                .HasIndex(e => e.Name)
                .IsUnique();

            modelBuilder.Entity<MessageAuditLog>()
                .HasIndex(e => e.CorrelationId);
        }

        private Task<List<T>> ExecuteProcInterpolatedAsync<T>(FormattableString query) where T : class
        {
            var result = this.Set<T>().FromSqlInterpolated(query).ToList();
            return Task.FromResult(result);
        }

        private Task<List<T>> ExecuteProcRawAsync<T>(string query) where T : class
        {
            var result = this.Set<T>().FromSqlRaw(query).ToList();
            return Task.FromResult(result);
        }
    }
}
