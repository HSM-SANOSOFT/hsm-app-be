import {
  SuccessResponseDto,
  UnsuccessResponseDto,
} from '@hsm-lib/definitions/dtos';

type MetadataOf<T> = NonNullable<SuccessResponseDto<T>['metadata']>;

export type ISuccessResponse<T> = Pick<SuccessResponseDto<T>, 'data'> & {
  metadata?: Pick<MetadataOf<T>, 'extra'>;
};
export type IUnsuccessResponse = Pick<UnsuccessResponseDto, 'issue'>;
