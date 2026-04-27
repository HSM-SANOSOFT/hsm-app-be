import { ApiProperty, ApiSchema } from '@nestjs/swagger';
import { IsNotEmpty, IsString } from 'class-validator';

@ApiSchema({ name: 'Get Template Request' })
export class GetTemplateRequestDto {
  @IsNotEmpty()
  @IsString()
  @ApiProperty({
    description:
      'Identificador de la plantilla: puede ser el UUID o el nombre de la plantilla',
    required: true,
    examples: {
      uuid: { value: '123e4567-e89b-12d3-a456-426614174000' },
      name: { value: 'my_template' },
    },
  })
  identifier: string;
}
