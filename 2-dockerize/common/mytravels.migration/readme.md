# Core DB Context
dotnet ef migrations add InitialCreate --startup-project ../../common/mytravels.migration --context CoreDbContext
dotnet ef database update --project ../../common/mytravels.domain --startup-project ../../common/mytravels.migration --context CoreDbContext

# Monitoring DB Context
dotnet ef migrations add InitialCreate --startup-project ../../common/mytravels.migration --context MonitoringDbContext
dotnet ef database update --project ../../domain/btms.domain.monitoring --startup-project ../../common/mytravels.migration --context MonitoringDbContext

# Permissions DB Context
dotnet ef migrations add InitialCreate --startup-project ../../common/mytravels.migration --context PermissionsDbContext
dotnet ef database update --project ../../domain/btms.domain.permissions --startup-project ../../common/mytravels.migration --context PermissionsDbContext

 