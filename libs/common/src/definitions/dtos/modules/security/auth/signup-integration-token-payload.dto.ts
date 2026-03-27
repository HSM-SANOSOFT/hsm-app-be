import { ApiSchema } from '@nestjs/swagger';
import { CreateUserIntegrationPayloadDto } from '../../core/users/create-user-integration-payload.dto';

@ApiSchema({ name: 'Sign up Integration User Payload' })
export class SignupIntegrationTokenPayloadDto extends CreateUserIntegrationPayloadDto {}
