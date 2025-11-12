import {
  FilterDto,
  PaginationDto,
  SuccessResponseDto,
  UnsuccessResponseDto,
} from '@hsm-lib/definitions/dtos';
import { applyDecorators, HttpStatus } from '@nestjs/common';
import { Reflector } from '@nestjs/core';
import {
  ApiBadGatewayResponse,
  ApiBadRequestResponse,
  ApiBearerAuth,
  ApiExtraModels,
  ApiForbiddenResponse,
  ApiInternalServerErrorResponse,
  ApiNotFoundResponse,
  ApiOkResponse,
  ApiUnauthorizedResponse,
  getSchemaPath,
} from '@nestjs/swagger';
import { IS_PUBLIC_KEY } from '../../modules/security/auth/auth.decorator';

export const ApiDocumentation = <
  TModel extends new (
    ...args: unknown[]
  ) => unknown,
>(
  model?: TModel,
  hasPagination: boolean = false,
  hasFilter: boolean = false,
  hasSort: boolean = false,
) => {
  return (
    target: object,
    propertyKey: string | symbol,
    descriptor: PropertyDescriptor,
  ) => {
    const reflector = new Reflector();
    const isPublic = reflector.getAllAndOverride<boolean>(IS_PUBLIC_KEY, [
      descriptor.value,
      target.constructor,
    ]);

    const successResponse = () => ({
      allOf: [
        { $ref: getSchemaPath(SuccessResponseDto<TModel>) },
        {
          properties: {
            metadata: {
              properties: {
                statusCode: {
                  type: 'number',
                  example: HttpStatus.OK,
                },
                extra:
                  hasPagination || hasFilter || hasSort
                    ? {
                        properties: {
                          filter: hasFilter
                            ? {}
                            : { type: 'array', example: undefined },
                          pagination: hasPagination
                            ? {}
                            : { type: 'array', example: undefined },
                          sort: hasSort
                            ? {}
                            : { type: 'array', example: undefined },
                        },
                      }
                    : { type: 'array', example: undefined },
              },
            },
            data: model
              ? { $ref: getSchemaPath(model) }
              : {
                  type: 'array',
                  example: undefined,
                },
          },
        },
      ],
    });

    const unsuccessSchema = (code: number, message: string) => ({
      allOf: [
        { $ref: getSchemaPath(UnsuccessResponseDto) },
        {
          properties: {
            metadata: {
              properties: {
                success: {
                  type: 'boolean',
                  example: false,
                },
                statusCode: {
                  type: 'number',
                  example: code,
                },
                message: {
                  type: 'string',
                  example: message,
                },
              },
            },
          },
        },
      ],
    });

    const decorators: Array<
      ClassDecorator | MethodDecorator | PropertyDecorator
    > = [
      ApiExtraModels(SuccessResponseDto<TModel>, UnsuccessResponseDto),
      ApiBadRequestResponse({
        description: 'Bad Request',
        schema: unsuccessSchema(HttpStatus.BAD_REQUEST, 'bad request'),
      }),
      ApiUnauthorizedResponse({
        description: 'Unauthorized',
        schema: unsuccessSchema(HttpStatus.UNAUTHORIZED, 'unauthorized'),
      }),
      ApiForbiddenResponse({
        description: 'Forbidden',
        schema: unsuccessSchema(HttpStatus.FORBIDDEN, 'forbidden'),
      }),
      ApiNotFoundResponse({
        description: 'Not Found',
        schema: unsuccessSchema(HttpStatus.NOT_FOUND, 'not found'),
      }),
      ApiInternalServerErrorResponse({
        description: 'Internal Server Error',
        schema: unsuccessSchema(
          HttpStatus.INTERNAL_SERVER_ERROR,
          'internal server error',
        ),
      }),
      ApiBadGatewayResponse({
        description: 'Bad Gateway',
        schema: unsuccessSchema(HttpStatus.BAD_GATEWAY, 'bad gateway'),
      }),
    ];

    if (model) {
      decorators.unshift(ApiExtraModels(model));
      decorators.unshift(
        ApiOkResponse({
          description: 'Successful response',
          schema: successResponse(),
        }),
      );
    } else {
      decorators.unshift(
        ApiOkResponse({
          description: 'Successful response',
          schema: successResponse(),
        }),
      );
    }
    if (!isPublic) {
      decorators.unshift(ApiBearerAuth());
    }

    applyDecorators(...decorators)(target, propertyKey, descriptor);
  };
};
