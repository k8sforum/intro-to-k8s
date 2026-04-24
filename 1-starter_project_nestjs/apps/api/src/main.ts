import 'reflect-metadata';
import { NestFactory } from '@nestjs/core';
import { ValidationPipe } from '@nestjs/common';
import { SwaggerModule, DocumentBuilder } from '@nestjs/swagger';
import { AppModule } from './app.module';
import { GlobalExceptionFilter } from './middleware/exception.filter';

async function bootstrap() {
  const app = await NestFactory.create(AppModule);

  app.useGlobalFilters(new GlobalExceptionFilter());
  app.useGlobalPipes(new ValidationPipe({ whitelist: true }));

  const corsHosts = process.env.CORS_HOSTS ?? '';
  const corsOrigin = corsHosts ? corsHosts.split(';').filter(Boolean) : '*';
  app.enableCors({
    origin: corsOrigin,
    methods: ['GET', 'POST', 'PUT', 'DELETE'],
    allowedHeaders: '*',
  });

  if (process.env.NODE_ENV === 'production') {
    app.use((_req, res, next) => {
      res.setHeader('Strict-Transport-Security', 'max-age=31536000; includeSubDomains');
      res.setHeader('X-Content-Type-Options', 'nosniff');
      next();
    });
  }

  const document = SwaggerModule.createDocument(app, new DocumentBuilder()
    .setTitle('MyTravels API')
    .setVersion('v1')
    .build());
  SwaggerModule.setup('swagger', app, document);

  const port = process.env.PORT ?? 5101;
  await app.listen(port);
}

bootstrap();
