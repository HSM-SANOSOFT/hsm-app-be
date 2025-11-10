import { BaseResponseDto, SuccessResponseDto } from '@hsm-lib/definitions/dtos';
import { ISuccessResponse } from '@hsm-lib/definitions/interfaces';
import {
  CallHandler,
  ExecutionContext,
  Injectable,
  NestInterceptor,
} from '@nestjs/common';
import { Request, Response } from 'express';
import { map, Observable } from 'rxjs';
import { extractApiVersion } from '../services';
@Injectable()
export class ResponseInterceptor<T>
  implements NestInterceptor<T, SuccessResponseDto<T>>
{
  intercept(
    ctx: ExecutionContext,
    next: CallHandler,
  ): Observable<SuccessResponseDto<T>> {
    const http = ctx.switchToHttp();
    const request: Request = http.getRequest<Request>();
    const response: Response = http.getResponse<Response>();
    const statusCode = response.statusCode;

    const baseResponse: BaseResponseDto = {
      success: statusCode >= 200 && statusCode < 300,
      statusCode,
      timestamp: new Date().toISOString(),
      path: request.url,
      message: 'Request processed successfully.',
      apiVersion: extractApiVersion(request),
    };
    return next.handle().pipe(
      map((payload: T | ISuccessResponse<T>) => {
        const hasData =
          payload instanceof Object && Object.hasOwn(payload, 'data');
        const hasMetadata =
          payload instanceof Object &&
          Object.hasOwn(payload, 'metadata') &&
          Object.keys(payload.metadata as Record<string, unknown>).length > 0;
        const data: T = hasData
          ? (payload as ISuccessResponse<T>).data
          : (payload as T);
        let metadata: ISuccessResponse<T>['metadata'] | undefined = hasMetadata
          ? (payload as ISuccessResponse<T>).metadata
          : undefined;

        if (
          Array.isArray(data) &&
          (metadata?.pagination == undefined ||
            Object.keys(metadata?.pagination).length === 0)
        ) {
          metadata = {
            ...metadata,
            pagination: {
              page: 1,
              pageSize: data.length,
              totalItems: data.length,
              totalPages: 1,
            },
          };
        }
        const formatedResponse: SuccessResponseDto<typeof data> = {
          ...baseResponse,
          data,
          metadata: metadata || undefined,
        };
        return formatedResponse;
      }),
    );
  }
}
