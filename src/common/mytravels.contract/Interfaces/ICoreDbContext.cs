using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using mytravels.contract.Dtos;
using mytravels.contract.Entities;
using mytravels.contract.Responses;
using Tag = mytravels.contract.Entities.Tag;

namespace mytravels.domain;

public interface ICoreDbContext
{
    DatabaseFacade Database { get; }
    DbSet<PointOfInterest> PointOfInterests { get; set; }
    DbSet<PointOfInterestTagAssociation> PointOfInterestTagAssociations { get; set; }
    DbSet<Tag> Tags { get; set; }
    DbSet<PointOfInterestAuditLog> PointOfInterestAuditLogs { get; set; }
    DbSet<PointOfInterestType> PointOfInterestTypes { get; set; }
    DbSet<PointOfInterestStatus> PointOfInterestStatuses { get; set; }
    EntityEntry Entry(object entity);
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
    Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    int SaveChanges();
    ChangeTracker ChangeTracker { get; }
    Task ExecuteSqlInterpolatedAsync(FormattableString sql, CancellationToken cancellationToken);
    void AddObject(object entity);
    void DeleteObject(object entity);
    void DetachObject(object entity);
    Task<List<PointOfInterest>> GetPointsOfInterestAsync(CancellationToken cancellationToken);
    Task<int> UpdatePointOfInterestTagsAsync(List<SavePointOfInterestDto> dtos, CancellationToken cancellationToken);
    Task<int> CreatePointOfInterestAsync(PointOfInterest point, CancellationToken cancellationToken);
    Task AddImageToPointOfInterestAsync(string blobName, PointOfInterest point, CancellationToken cancellationToken);
    Task UpdatePointOfInterestStatusAsync(int pointOfInterestStatusId, PointOfInterest point, CancellationToken cancellationToken);
    Task UpdateAddressAsync(UpdateAddressDto dto, CancellationToken cancellationToken);
    Task<List<GetPointOfInterestResponse>> GetPointsOfInterestByTagAsync(string tagName, CancellationToken cancellationToken);
    Task<List<GetPointOfInterestResponse>> GetPointsOfInterestByKeyAsync(string pointOfInterestKey, CancellationToken cancellationToken);
    Task<List<GetPointOfInterestResponse>> GetAllPointsOfInterestAsync(CancellationToken cancellationToken);
}
