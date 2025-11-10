import { ApiProperty } from '@nestjs/swagger';
import { IsNotEmpty, IsString } from 'class-validator';

export class LogoutIntegrationTokenPayloadDto {
  @ApiProperty({ required: true })
  @IsString()
  @IsNotEmpty()
  token: string;
}
