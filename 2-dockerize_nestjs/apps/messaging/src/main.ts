import 'reflect-metadata';
import { NestFactory } from '@nestjs/core';
import { AppModule } from './app.module';

async function bootstrap() {
  const app = await NestFactory.create(AppModule);
  const port = process.env.PORT ?? 5102;
  await app.listen(port);
  console.log(`Messaging service is running on port ${port}...`);
}

bootstrap();
