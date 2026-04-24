import { ApiErrorDto } from '../dto/api-error.dto';

export class ApiException extends Error {
  readonly statusCode: number;
  readonly apiError?: ApiErrorDto;

  constructor(statusCodeOrDto: number | ApiErrorDto, message?: string) {
    if (typeof statusCodeOrDto === 'number') {
      super(message);
      this.statusCode = statusCodeOrDto;
    } else {
      const dto = statusCodeOrDto;
      super(`API call [${dto.links}] failed, status code [${dto.httpStatusCode}], message [${dto.message}]`);
      this.statusCode = dto.httpStatusCode;
      this.apiError = dto;
    }
    this.name = 'ApiException';
  }
}
