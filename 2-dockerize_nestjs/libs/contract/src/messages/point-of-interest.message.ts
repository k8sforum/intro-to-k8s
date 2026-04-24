import { IMessage } from '../interfaces/message.interface';

export class PointOfInterestMessage implements IMessage {
  correlationId: string = '';
  pointOfInterestId: number;
}
