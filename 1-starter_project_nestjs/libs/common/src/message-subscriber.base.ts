import { OnModuleInit, Logger } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import * as amqplib from 'amqplib';
import { IMessage } from '@mytravels/contract';

export abstract class MessageSubscriberBase<T extends IMessage> implements OnModuleInit {
  protected readonly logger: Logger;

  constructor(
    private readonly config: ConfigService,
    private readonly exchangeName: string,
    private readonly queueName: string,
  ) {
    this.logger = new Logger(this.constructor.name);
  }

  async onModuleInit(): Promise<void> {
    const uri = this.config.get<string>('RABBIT_MQ_URI');
    const connection = await amqplib.connect(uri);
    const channel = await connection.createChannel();

    await channel.assertExchange(this.exchangeName, 'fanout');
    await channel.assertQueue(this.queueName);
    await channel.bindQueue(this.queueName, this.exchangeName, '');
    await channel.prefetch(10);

    this.logger.log(`Consuming queue '${this.queueName}' on exchange '${this.exchangeName}'`);

    channel.consume(this.queueName, async (msg) => {
      if (!msg) return;
      try {
        const content = msg.content.toString();
        if (!content) return;
        const payload: T = JSON.parse(content);
        await this.processMessage(payload);
        channel.ack(msg);
      } catch (err) {
        this.logger.error('Error processing message', err);
        channel.nack(msg, false, false);
      }
    });
  }

  protected abstract processMessage(message: T): Promise<void>;
}
