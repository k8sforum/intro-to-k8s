import { Module } from '@nestjs/common';
import { ConfigModule } from '@nestjs/config';
import { PointOfInterestModule } from './point-of-interest/point-of-interest.module';
import { AppController } from './app.controller';

@Module({
  imports: [
    ConfigModule.forRoot({ isGlobal: true }),
    PointOfInterestModule,
  ],
  controllers: [AppController],
})
export class AppModule {}
