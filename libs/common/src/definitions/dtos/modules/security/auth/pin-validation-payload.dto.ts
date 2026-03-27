import { ApiProperty, ApiSchema } from '@nestjs/swagger';
import { IsNotEmpty, IsNumber } from 'class-validator';
import { PinGenerationPayloadDto } from '.';

@ApiSchema({ name: 'PIN Validation Payload' })
export class PinValidationPayloadDto extends PinGenerationPayloadDto {
  @IsNotEmpty()
  @IsNumber()
  @ApiProperty({ description: 'Código PIN a validar', required: true })
  code: number;
}
