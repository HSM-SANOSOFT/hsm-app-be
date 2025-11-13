import { ApiProperty, ApiSchema } from '@nestjs/swagger';
import { IsNotEmpty, IsString } from 'class-validator';

@ApiSchema({ name: 'Logout Payload' })
export class LogoutPayloadDto {
  @IsNotEmpty()
  @IsString()
  @ApiProperty({ required: true })
  id: string;
}
