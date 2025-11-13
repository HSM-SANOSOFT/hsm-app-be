import { ApiDocumentation, Public } from '@hsm-lib/common/decorator';
import { ISuccessResponse } from '@hsm-lib/definitions/interfaces';
import { Controller, Get } from '@nestjs/common';
import { ApiProperty } from '@nestjs/swagger';
import { IsNotEmpty, IsString } from 'class-validator';

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
  @ApiDocumentation(AvailabilityDto)
  @Public()
  @Get()
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
