export class OutOfRadiusException extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'OutOfRadiusException';
  }
}
