cd '../../src/common/mytravels.migration/'

dotnet tool restore

#dotnet ef --startup-project ../../common/mytravels.migration/ --project ../../common/mytravels.domain/ database drop --context CoreDbContext -f -v
# dotnet ef --startup-project ../../common/mytravels.migration/ --project ../../common/mytravels.domain/  migrations add Init  --context CoreDbContext
dotnet ef --startup-project ../../common/mytravels.migration/ --project ../../common/mytravels.domain/ migrations add UpdateStoredProcedure --context CoreDbContext
dotnet ef --startup-project ../../common/mytravels.migration/ --project ../../common/mytravels.domain/ migrations add SeedData --context CoreDbContext
# dotnet ef --startup-project ../../common/mytravels.migration/ --project ../../common/mytravels.domain/ database update --context CoreDbContext
# dotnet ef --startup-project ../../common/mytravels.migration/ --project ../../common/mytravels.domain/ migrations  script -i  --context CoreDbContext -o C:\development\temp\Migrations\migration.sql
