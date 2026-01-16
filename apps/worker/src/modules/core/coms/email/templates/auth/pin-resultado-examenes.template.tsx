import { PinResultadoExamenTemplateDto } from '@hsm-lib/definitions/dtos';

export function PinResultadoExamenTemplate(
  data: PinResultadoExamenTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Estimado paciente <i>{data.patientName}</i>,
        <br />
        <br />
        <b>Hospital Santamaría</b> te informa que en la fecha <b>{data.date}</b>
        , solicitaste un pin para acceder a tus exámenes o informes médicos a
        través de nuestro sistema.
        <br />
        <br />
        Este contiene un código de verificación necesario para identificarse en
        nuestro sistema. Este código es crucial para garantizar la seguridad del
        sistema y la privacidad de tu información médica.
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
        Recuerda que la protección de tu información médica es tu
        responsabilidad. Por ningún motivo la compartas con terceros.
        <br />
        <br />
        Gracias por su colaboración.
        <br />
        Atentamente,
        <br />
        Hospital Santamaría
      </p>
    </>
  );
}
