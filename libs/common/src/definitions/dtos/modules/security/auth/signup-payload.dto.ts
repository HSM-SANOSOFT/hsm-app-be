import { ApiSchema } from '@nestjs/swagger';
import { CreateUserPayloadDto } from '../../core/users/create-user-payload.dto';

@ApiSchema({ name: 'Sign Up User Payload' })
export class SignupPayloadDto extends CreateUserPayloadDto {}
