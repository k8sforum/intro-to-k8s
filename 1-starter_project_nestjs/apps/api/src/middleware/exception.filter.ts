import {
  ExceptionFilter, Catch, ArgumentsHost, HttpStatus, Logger,
} from '@nestjs/common';
import { Response, Request } from 'express';
import { v4 as uuidv4 } from 'uuid';
import {
  ApiException, DataNotFoundException, EntityNotFoundException,
  RequiredParameterNotFoundException, OutOfRadiusException, ApiErrorDto,
} from '@mytravels/contract';

@Catch()
export class GlobalExceptionFilter implements ExceptionFilter {
  private readonly logger = new Logger(GlobalExceptionFilter.name);

  catch(exception: unknown, host: ArgumentsHost) {
    const ctx = host.switchToHttp();
    const response = ctx.getResponse<Response>();
    const request = ctx.getRequest<Request>();

    let statusCode = HttpStatus.INTERNAL_SERVER_ERROR;
    let isClientError = false;

    if (exception instanceof RequiredParameterNotFoundException || exception instanceof OutOfRadiusException) {
      statusCode = HttpStatus.FORBIDDEN;
      isClientError = true;
    } else if (exception instanceof DataNotFoundException || exception instanceof EntityNotFoundException) {
      statusCode = HttpStatus.NOT_FOUND;
      isClientError = true;
    } else if (exception instanceof ApiException) {
      statusCode = exception.statusCode;
    }

    const message = exception instanceof Error ? exception.message : 'An unexpected error occurred';
    const errorId = uuidv4().replace(/-/g, '');

    const error: ApiErrorDto = {
      id: errorId,
      httpStatusCode: statusCode,
      message,
      title: isClientError ? message : 'An error occurred in the API. Please use the id and contact our support team if the error persists.',
      links: request.url,
      code: '',
      detail: '',
    };

    if (isClientError) {
      this.logger.error(`Client error ${errorId} -- ${message}`);
    } else {
      this.logger.error(`Server error ${errorId} -- ${message}`, exception instanceof Error ? exception.stack : '');
    }

    response.status(statusCode).json(error);
  }
}
