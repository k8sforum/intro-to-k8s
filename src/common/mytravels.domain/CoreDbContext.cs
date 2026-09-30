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
        public DbSet<FailedMessage> FailedMessages { get; set; }
        public void DetachObject(object entity) => Entry(entity).State = EntityState.Detached;
        public void DeleteObject(object entity) => Entry(entity).State = EntityState.Deleted;
        public void AddObject(object entity) => Entry(entity).State = EntityState.Added;

        public async Task ExecuteSqlInterpolatedAsync(FormattableString sql, CancellationToken cancellationToken)
            => await this.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);

        public async Task<List<PointOfInterest>> GetPointsMissingAddressAsync(DateTime createdAfter, CancellationToken cancellationToken)
            => await this.PointOfInterests
                .Where(x => x.DateCreated > createdAfter && (x.FormattedAddress == null || x.FormattedAddress.Trim() == ""))
                .ToListAsync(cancellationToken);

        public async Task<PointOfInterest> GetLatestPointOfInterestByKeyAsync(string pointOfInterestKey, CancellationToken cancellationToken)
            => await this.PointOfInterests
                .Where(x => x.PointOfInterestKey == pointOfInterestKey)
                .OrderByDescending(x => x.DateCreated)
                .FirstOrDefaultAsync(cancellationToken);

        public async Task<List<GetPointOfInterestResponse>> GetPointsOfInterestByKeyAsync(string pointOfInterestKey, CancellationToken cancellationToken)
            => await ExecuteProcInterpolatedAsync<GetPointOfInterestResponse>($"SELECT * FROM public.spGetPointOfInterestById({pointOfInterestKey})", cancellationToken);

        public async Task<List<GetPointOfInterestResponse>> GetAllPointsOfInterestAsync(CancellationToken cancellationToken)
            => await ExecuteProcRawAsync<GetPointOfInterestResponse>("SELECT * FROM public.spGetPointOfInterest()", cancellationToken);

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

        public async Task<List<FailedMessageDto>> GetFailedMessagesAsync(CancellationToken cancellationToken)
        {
            return await this.FailedMessages
                .Where(x => x.ResolvedAt == null)
                .OrderByDescending(x => x.FailedAt)
                .Select(x => new FailedMessageDto
                {
                    Id = x.Id,
                    CorrelationId = x.CorrelationId,
                    OriginalExchange = x.OriginalExchange,
                    PointOfInterestId = x.PointOfInterestId,
                    ErrorMessage = x.ErrorMessage,
                    RetryCount = x.RetryCount,
                    FailedAt = x.FailedAt
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<FailedMessage> GetFailedMessageByIdAsync(int id, CancellationToken cancellationToken)
            => await this.FailedMessages.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        public async Task MarkFailedMessageResolvedAsync(int id, CancellationToken cancellationToken)
        {
            FailedMessage message = await this.FailedMessages.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (message is null) return;

            message.ResolvedAt = DateTime.UtcNow;
            var entry = this.Entry(message);
            entry.State = EntityState.Unchanged;
            entry.Property(nameof(message.ResolvedAt)).IsModified = true;
            await this.SaveChangesAsync(cancellationToken);
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
            List<PointOfInterest> points = await this.PointOfInterests
                .Where(x => x.PointOfInterestKey == dto.PointOfInterestKey)
                .ToListAsync(cancellationToken);
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

            modelBuilder.Entity<FailedMessage>()
                .HasIndex(e => e.ResolvedAt);
        }

        private async Task<List<T>> ExecuteProcInterpolatedAsync<T>(FormattableString query, CancellationToken cancellationToken) where T : class
            => await this.Set<T>().FromSqlInterpolated(query).ToListAsync(cancellationToken);

        private async Task<List<T>> ExecuteProcRawAsync<T>(string query, CancellationToken cancellationToken) where T : class
            => await this.Set<T>().FromSqlRaw(query).ToListAsync(cancellationToken);
    }
}
