// libs/common/decorators/api-response.decorator.ts

import {
  SuccessResponseDto,
  UnsuccessResponseDto,
} from '@hsm-lib/definitions/dtos';
import { applyDecorators } from '@nestjs/common';
import {
  ApiBadRequestResponse,
  ApiExtraModels,
  ApiForbiddenResponse,
  ApiInternalServerErrorResponse,
  ApiNotFoundResponse,
  ApiOkResponse,
  ApiUnauthorizedResponse,
  getSchemaPath,
} from '@nestjs/swagger';
export const ApiDocumentation = <
  TModel extends new (
    ...args: unknown[]
  ) => unknown,
>(
  model: TModel,
) =>
  applyDecorators(
    ApiExtraModels(SuccessResponseDto<TModel>, UnsuccessResponseDto, model),
    ApiOkResponse({
      description: 'Successful response',
      schema: {
        allOf: [
          { $ref: getSchemaPath(SuccessResponseDto<TModel>) },
          {
            properties: {
              data: { $ref: getSchemaPath(model) },
            },
          },
        ],
      },
    }),
    ApiBadRequestResponse({
      description: 'Bad Request',
      schema: { $ref: getSchemaPath(UnsuccessResponseDto) },
    }),
    ApiUnauthorizedResponse({
      description: 'Unauthorized',
      schema: { $ref: getSchemaPath(UnsuccessResponseDto) },
    }),
    ApiForbiddenResponse({
      description: 'Forbidden',
      schema: { $ref: getSchemaPath(UnsuccessResponseDto) },
    }),
    ApiNotFoundResponse({
      description: 'Not Found',
      schema: { $ref: getSchemaPath(UnsuccessResponseDto) },
    }),
    ApiInternalServerErrorResponse({
      description: 'Internal Server Error',
      schema: { $ref: getSchemaPath(UnsuccessResponseDto) },
    }),
  );
