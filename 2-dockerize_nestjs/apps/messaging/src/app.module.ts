import { Module } from '@nestjs/common';
import { ConfigModule } from '@nestjs/config';
import { CoreDbModule } from '@mytravels/domain';
import { CommonModule } from '@mytravels/common';
import { StorageModule } from '@mytravels/storage';
import { AppendFormattedAddressHandler } from './append-formatted-address.handler';
import { AppendFormattedAddressSweeper } from './append-formatted-address-sweeper';
import { ResizeImageHandler } from './resize-image.handler';

@Module({
  imports: [
    ConfigModule.forRoot({ isGlobal: true }),
    CoreDbModule,
    CommonModule,
    StorageModule,
  ],
  providers: [AppendFormattedAddressHandler, AppendFormattedAddressSweeper, ResizeImageHandler],
})
export class AppModule {}
