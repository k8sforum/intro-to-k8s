import 'reflect-metadata';
import { AppDataSource } from '../libs/domain/src/data-source';

async function runMigrations() {
  console.log('Initialising data source...');
  await AppDataSource.initialize();
  console.log('Running pending migrations...');
  const migrations = await AppDataSource.runMigrations();
  if (migrations.length === 0) {
    console.log('No pending migrations.');
  } else {
    migrations.forEach((m) => console.log(`  Applied: ${m.name}`));
  }
  await AppDataSource.destroy();
  console.log('Done.');
}

runMigrations().catch((err) => {
  console.error('Migration failed:', err);
  process.exit(1);
});
