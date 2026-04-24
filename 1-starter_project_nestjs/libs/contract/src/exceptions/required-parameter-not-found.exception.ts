export class RequiredParameterNotFoundException extends Error {
  readonly paramName: string;

  constructor(name: string) {
    super(`A parameter with name '${name}' cannot be null or empty`);
    this.name = 'RequiredParameterNotFoundException';
    this.paramName = name;
  }
}
