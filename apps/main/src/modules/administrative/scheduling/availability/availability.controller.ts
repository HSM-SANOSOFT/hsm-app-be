import { LoginPayloadDto } from '@hsm-lib/definitions/dtos';
import { ISuccessResponse } from '@hsm-lib/definitions/interfaces';
import { Body, Controller, Get } from '@nestjs/common';
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
  availability(@Body() _body: LoginPayloadDto) {
    const data = [
      {
        name: 'test',
      },
    ];
    const successResponse: ISuccessResponse<typeof data> = {
      data,
      metadata: {
        extra: {},
      },
    };
    return successResponse;
  }
}
