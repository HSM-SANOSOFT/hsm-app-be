import { MetadataDto, UnsuccessResponseDto } from '@hsm-lib/definitions/dtos';
import { IUnsuccessResponse } from '@hsm-lib/definitions/interfaces';
import {
  ArgumentsHost,
  Catch,
  ExceptionFilter,
  HttpException,
} from '@nestjs/common';
import { Request, Response } from 'express';
import { extractApiVersion } from '../services';

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === 'object' && v !== null;
}
function isStringArray(v: unknown): v is string[] {
  return Array.isArray(v) && v.every(x => typeof x === 'string');
}

@Catch(HttpException)
export class ResponseFilter implements ExceptionFilter {
  catch(exception: HttpException, host: ArgumentsHost) {
    const ctx = host.switchToHttp();
    const res = ctx.getResponse<Response>();
    const req = ctx.getRequest<Request>();

    const status = exception.getStatus();
    const payload = exception.getResponse();
    const hasIssue = isRecord(payload) && Object.hasOwn(payload, 'issue');

    let issue: IUnsuccessResponse;

    if (hasIssue) {
      issue = payload['issue'] as IUnsuccessResponse;
    } else {
      issue = { issue: { message: exception.message } };
      if (typeof payload === 'string') {
        issue = {
          issue: {
            message: payload,
          },
        };
      } else if (isRecord(payload)) {
        const msg = payload['message'];
        const err = payload['error'];
        const code =
          (typeof payload['code'] === 'string' && payload['code']) ||
          (typeof payload['errorCode'] === 'string' && payload['errorCode']) ||
          (typeof payload['error_code'] === 'string' &&
            payload['error_code']) ||
          undefined;
        const detail =
          (typeof payload['detail'] === 'string' && payload['detail']) ||
          (typeof payload['cause'] === 'string' && payload['cause']) ||
          (typeof payload['stack'] === 'string' && payload['stack']) ||
          undefined;

        if (typeof msg === 'string') {
          issue.issue.message = msg;
        } else if (isStringArray(msg)) {
          issue.issue.message = msg;
        }

        if (typeof err === 'string') {
          issue.issue.error = err;
        }

        if (code) issue.issue.code = code;
        if (detail) issue.issue.detail = detail;

        const errors = payload['errors'];
        if (Array.isArray(errors)) {
          const props = errors
            .filter(
              e =>
                isRecord(e) &&
                typeof e['property'] === 'string' &&
                e['property'].length > 0,
            )
            .map(e => String(e['property']));
          if (props.length === 1) {
            issue.issue.field = props[0];
          } else if (props.length > 1) {
            issue.issue.field = Array.from(new Set(props));
          }
        } else if (isStringArray(msg)) {
          const inferred = msg
            .map(s => s.split(' ')[0])
            .filter(t => /^[a-zA-Z0-9_.-]+$/.test(t));

          if (inferred.length === 1) {
            issue.issue.field = inferred[0];
          } else if (inferred.length > 1) {
            issue.issue.field = Array.from(new Set(inferred));
          }
        }
      }
    }

    const metadata: MetadataDto = {
      success: false,
      statusCode: status,
      timestamp: new Date().toISOString(),
      path: req.url,
      message: 'Request processed unsuccessfully.',
      apiVersion: extractApiVersion(req),
    };

    const body: UnsuccessResponseDto = { metadata, issue: issue.issue };
    res.status(status).json(body);
  }
}
