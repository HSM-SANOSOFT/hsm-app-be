import { ISuccessResponse } from '@hsm-lib/definitions/interfaces';
import { Controller, Get } from '@nestjs/common';
import { ApiProperty } from '@nestjs/swagger';
import { IsNotEmpty, IsString } from 'class-validator';
import { ApiDocumentation } from '../../../../common/decorator';
import { Public } from '../../../security/auth/auth.decorator';

class AvailabilityDto {
  @ApiProperty({
    description: 'Name of the availability',
    example: 'test',
  })
  @IsString()
  @IsNotEmpty()
  name: string;
}

@Controller('scheduling/availability')
export class AvailabilityController {
  @Public()
  @Get()
  @ApiDocumentation(AvailabilityDto)
  availability() {
    const successResponse: ISuccessResponse<AvailabilityDto> = {
      data: {
        name: 'test',
      },
      metadata: {
        extra: {},
      },
    };
    return successResponse;
  }
}
