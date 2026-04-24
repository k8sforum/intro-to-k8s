export interface IMessagePublisher {
  publish<T>(exchange: string, message: T): Promise<void>;
}

export const IMessagePublisher = Symbol('IMessagePublisher');
