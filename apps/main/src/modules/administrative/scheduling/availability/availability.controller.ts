import { LoginPayloadDto, PaginationDto } from '@hsm-lib/definitions/dtos';
import {
  ISuccessResponse,
  IUnsuccessResponse,
} from '@hsm-lib/definitions/interfaces';
import { BadRequestException, Body, Controller, Get } from '@nestjs/common';
import { Public } from '../../../security/auth/auth.decorator';

@Controller('scheduling/availability')
export class AvailabilityController {
  @Public()
  @Get()
  availability(@Body() _body: LoginPayloadDto) {
    const data = [
      {
        name: 'test',
      },
    ];
    const pagination: PaginationDto = {
      page: 1,
      pageSize: 10,
      totalItems: 10,
      totalPages: 1,
    };
    const successResponse: ISuccessResponse<typeof data> = {
      data,
      metadata: {
        pagination,
      },
    };

    const unsuccessResponse: IUnsuccessResponse = {
      error: {
        message: 'Invalid payload',
      },
    };

    throw new BadRequestException(unsuccessResponse);

    return successResponse;
  }
}
