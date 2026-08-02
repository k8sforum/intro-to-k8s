DROP USER IF EXISTS [sanorth-api-prod-mytravels-001];
CREATE USER [sanorth-api-prod-mytravels-001] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [sanorth-api-prod-mytravels-001];
ALTER ROLE db_datawriter ADD MEMBER [sanorth-api-prod-mytravels-001];
ALTER ROLE db_ddladmin ADD MEMBER [sanorth-api-prod-mytravels-001];
GRANT EXEC TO [sanorth-api-prod-mytravels-001];
GO