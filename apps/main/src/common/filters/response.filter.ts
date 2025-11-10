import {
  BaseResponseDto,
  UnsuccessResponseDto,
} from '@hsm-lib/definitions/dtos';
import { IUnsuccessResponse } from '@hsm-lib/definitions/interfaces';
import {
  ArgumentsHost,
  Catch,
  ExceptionFilter,
  HttpException,
} from '@nestjs/common';
import { Request, Response } from 'express';
import { extractApiVersion } from '../services';

const isPlainObject = (v: unknown): v is Record<string, unknown> => {
  if (v === null || typeof v !== 'object') return false;
  const p = Object.getPrototypeOf(v);
  return p === Object.prototype || p === null;
};

@Catch(HttpException)
export class ResponseFilter implements ExceptionFilter {
  catch(exception: HttpException, host: ArgumentsHost) {
    const ctx = host.switchToHttp();
    const response: Response = ctx.getResponse<Response>();
    const request: Request = ctx.getRequest<Request>();
    const status = exception.getStatus();

    const payload = exception.getResponse();
    let errorObj: IUnsuccessResponse['error'] = { message: exception.message };

    if (typeof payload === 'string') {
      errorObj = { message: payload };
    } else if (payload && typeof payload === 'object') {
      if (Object.hasOwn(payload, 'error')) {
        const e = (payload as any).error;
        if (isPlainObject(e)) errorObj = e;
        else if (Array.isArray(e)) errorObj = { message: e };
        else if (typeof e === 'string') errorObj = { message: e };
        else errorObj = { message: exception.message };
      }
      if (Object.hasOwn(payload, 'message')) {
        const m = (payload as any).message;
        errorObj = {
          ...(isPlainObject(errorObj) ? errorObj : {}),
          message: Array.isArray(m) ? m : m,
        };
      }
    }

    const baseResponse: BaseResponseDto = {
      success: false,
      statusCode: status,
      timestamp: new Date().toISOString(),
      path: request.url,
      message: 'Request processed unsuccessfully.',
      apiVersion: extractApiVersion(request),
    };

    const formatted: UnsuccessResponseDto = {
      ...baseResponse,
      error: errorObj,
    };

    response.status(status).json(formatted);
  }
}
