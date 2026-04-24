import { Injectable } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import * as amqplib from 'amqplib';
import { IMessagePublisher } from '@mytravels/contract';

@Injectable()
export class MessagePublisherService implements IMessagePublisher {
  private readonly uri: string;

  constructor(config: ConfigService) {
    this.uri = config.get<string>('RABBIT_MQ_URI');
  }

  async publish<T>(exchange: string, message: T): Promise<void> {
    const connection = await amqplib.connect(this.uri);
    const channel = await connection.createChannel();

     await channel.assertExchange(exchange, 'fanout');
    channel.publish(exchange, '', Buffer.from(JSON.stringify(message)));

    await channel.close();
    await connection.close();
  }
}
