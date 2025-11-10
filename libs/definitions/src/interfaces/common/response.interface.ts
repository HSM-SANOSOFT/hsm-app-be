import {
  SuccessResponseDto,
  UnsuccessResponseDto,
} from '@hsm-lib/definitions/dtos';

export type ISuccessResponse<T> = Pick<
  SuccessResponseDto<T>,
  'data' | 'metadata'
>;
export type IUnsuccessResponse = Pick<UnsuccessResponseDto, 'error'>;
