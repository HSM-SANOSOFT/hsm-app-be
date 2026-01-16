import { PinInicioSesionTemplateDto } from '@hsm-lib/definitions/dtos';

export function PinInicioSesionTemplate(
  data: PinInicioSesionTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Estimado colaborador,
        <br />
        <br />
        <b>SANOSOFT</b> te informa que en la fecha <b>{data.date}</b>,
        solicitaste un pin para el ingreso a nuestro sistema.
        <br />
        <br />
        Este contiene un código de verificación necesario para identificarse en
        nuestro sistema. Este código es crucial para garantizar la seguridad del
        sistema.
        <br />
        <br />
        Código de verificación: <b>{data.pin}</b>
        <br />
        <br />
        Por favor, tenga en cuenta que tiene un total de 5 intentos para
        ingresar el código correctamente. Si tiene algún problema o pregunta, no
        dude en contactarnos.
        <br />
        <br />
        Recuerda que el cuidado de la información de acceso al sistema{' '}
        <i>SANOSOFT</i> es tu responsabilidad. Por ningún motivo la compartas
        con terceros.
        <br />
        <br />
        Gracias por su colaboración.
        <br />
        Atentamente,
        <br />
        Departamento de Sistemas ext.517
      </p>
    </>
  );
}
